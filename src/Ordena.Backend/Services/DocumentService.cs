using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

internal sealed class DocumentService(
    IUnitOfWorkFactory uowFactory,
    IFileStorage storage,
    PartnerResolver partnerResolver,
    ArchiveMover archiveMover,
    IClock clock) : IDocumentService
{
    public Task<DocumentDetails> GetAsync(int id, CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        return (await LoadAsync(uow, id, ct).ConfigureAwait(false)).ToDetails();
    }, ct);

    public Task<Result<DocumentListItem>> EditAsync(EditDocumentRequest request, CancellationToken ct)
    {
        // The form requires these, so a missing one is a bug, not something to report to the user.
        if (string.IsNullOrWhiteSpace(request.PartnerName))
            throw new ArgumentException("The partner name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DocNumber))
            throw new ArgumentException("The document number is required.", nameof(request));

        return Task.Run(() => EditCoreAsync(request, ct), ct);
    }

    // Only the row changes, so it works while the archive folder is missing too.
    public Task<Result> SetPaidAsync(int id, bool isPaid, CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        var document = RequireActive(await LoadAsync(uow, id, ct).ConfigureAwait(false));
        document.IsPaid = isPaid;
        document.UpdatedAt = clock.UtcNow;
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Ok();
    }, ct);

    public Task<Result> DeleteAsync(int id, CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        var document = RequireActive(await LoadAsync(uow, id, ct).ConfigureAwait(false));
        var root = await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
        if (!root.IsSuccess)
            return Result.Fail(root.Error.Value, root.Detail);

        var now = clock.UtcNow;
        var trashPath = ArchiveNaming.ToTrash(document.RelativePath);
        return await archiveMover.MoveAsync(uow, document, trashPath, root.Value, d =>
        {
            d.DeletedAt = now;
            d.UpdatedAt = now;
        }, ct).ConfigureAwait(false);
    }, ct);

    public Task<Result> RestoreAsync(int id, CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        var document = await LoadAsync(uow, id, ct).ConfigureAwait(false);
        if (document.DeletedAt is null)
            throw new InvalidOperationException($"Document {id} is not in the trash.");
        var root = await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
        if (!root.IsSuccess)
            return Result.Fail(root.Error.Value, root.Detail);

        var now = clock.UtcNow;
        var standardPath = ArchiveNaming.BuildRelativePath(document, document.Partner);
        return await archiveMover.MoveAsync(uow, document, standardPath, root.Value, d =>
        {
            d.DeletedAt = null;
            d.UpdatedAt = now;
        }, ct).ConfigureAwait(false);
    }, ct);

    public Task<IReadOnlyList<DocumentDetails>> ListTrashAsync(CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        return await uow.Documents.ListDeletedAsync(ct).ConfigureAwait(false);
    }, ct);

    public Task<Result<string>> GetFullPathAsync(int id, CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        var document = await LoadAsync(uow, id, ct).ConfigureAwait(false);
        var root = await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
        if (!root.IsSuccess)
            return root;

        var path = ArchiveNaming.ToFullPath(root.Value, document.RelativePath);
        return storage.FileExists(path) ? Result.Ok(path) : Result.Fail<string>(ErrorCode.FileMissing, path);
    }, ct);

    public Task<Result> RelinkAsync(int id, string path, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException($"The file must be an absolute path: '{path}'.", nameof(path));

        return Task.Run(async () =>
        {
            await using var uow = uowFactory.Create();
            var document = await LoadAsync(uow, id, ct).ConfigureAwait(false);
            var root = await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
            if (!root.IsSuccess)
                return Result.Fail(root.Error.Value, root.Detail);

            var read = await storage.ReadAsync(path, ct).ConfigureAwait(false);
            if (!read.IsSuccess)
                return Result.Fail(read.Error.Value, read.Detail);
            if (Sha256Hasher.Compute(read.Value) != document.Sha256)
                return Result.Fail(ErrorCode.NotSameFile, path);

            // The row doesn't change: the file goes back to where it says, so no journal is needed.
            var expected = ArchiveNaming.ToFullPath(root.Value, document.RelativePath);
            return ArchiveNaming.IsInArchive(root.Value, path)
                ? await storage.MoveAsync(path, expected, ct).ConfigureAwait(false)
                : await storage.WriteVerifiedAsync(expected, read.Value, document.Sha256, ct).ConfigureAwait(false);
        }, ct);
    }

    private async Task<Result<DocumentListItem>> EditCoreAsync(EditDocumentRequest request, CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        var document = RequireActive(await LoadAsync(uow, request.Id, ct).ConfigureAwait(false));

        var resolved = await partnerResolver.GetOrCreateAsync(uow, request.PartnerName, ct).ConfigureAwait(false);
        if (!resolved.IsSuccess)
            return Result.Fail<DocumentListItem>(resolved.Error.Value, resolved.Detail);
        var partner = resolved.Value;
        var partnerIsNew = partner.Id == 0;

        // The document's own number is not a duplicate of itself.
        var docNumber = request.DocNumber.Trim();
        var docNumberNorm = TextNormalizer.NormalizeNumber(docNumber);
        if (!request.ConfirmDuplicate &&
            await uow.Documents.NumberExistsAsync(partner.Id, docNumberNorm, document.Id, ct).ConfigureAwait(false))
            return Result.Fail<DocumentListItem>(ErrorCode.DuplicateNumber, docNumber);

        var newPath = ArchiveNaming.BuildRelativePath(
            request.IssueDate, request.Direction, request.Type, docNumber, partner.FolderName);
        var now = clock.UtcNow;
        void Apply(Document d)
        {
            d.Partner = partner;
            d.DocNumber = docNumber;
            d.DocNumberNorm = docNumberNorm;
            d.IssueDate = request.IssueDate;
            d.Type = request.Type;
            d.Direction = request.Direction;
            d.IsPaid = request.IsPaid;
            d.UpdatedAt = now;
        }

        var root = await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
        if (!root.IsSuccess)
        {
            // Without the archive folder, only a change that keeps the file name can be saved: marking it paid,
            // say, from Buscar while the disk is unplugged. Anything else would have to move the file. A new
            // partner was only staged, so returning drops it.
            if (!string.Equals(newPath, document.RelativePath, StringComparison.Ordinal))
                return Result.Fail<DocumentListItem>(root.Error.Value, root.Detail);
            Apply(document);
            await uow.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok(document.ToListItem());
        }

        var moved = await archiveMover.MoveAsync(uow, document, newPath, root.Value, Apply, ct).ConfigureAwait(false);

        if (!moved.IsSuccess)
        {
            // A new partner was saved with the journal row. The edit didn't happen, so nothing uses it.
            if (partnerIsNew)
            {
                uow.Partners.Remove(partner);
                await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            return Result.Fail<DocumentListItem>(moved.Error.Value, moved.Detail);
        }

        return Result.Ok(document.ToListItem());
    }

    // Tracked, with its partner.
    private static async Task<Document> LoadAsync(IUnitOfWork uow, int id, CancellationToken ct) =>
        await uow.Documents.GetByIdAsync(id, ct).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"There is no document {id}.");

    private static Document RequireActive(Document document) => document.DeletedAt is null
        ? document
        : throw new InvalidOperationException($"Document {document.Id} is in the trash; restore it first.");
}
