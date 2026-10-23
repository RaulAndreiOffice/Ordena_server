using System.Globalization;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

internal sealed class ImportService(
    IUnitOfWorkFactory uowFactory,
    IFileStorage storage,
    PartnerResolver partnerResolver,
    ArchiveMover archiveMover,
    IClock clock) : IImportService
{
    public Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        // The form requires these, so a missing one is a bug, not something to report to the user.
        if (!Path.IsPathFullyQualified(request.SourcePath))
            throw new ArgumentException($"The source must be an absolute path: '{request.SourcePath}'.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.PartnerName))
            throw new ArgumentException("The partner name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DocNumber))
            throw new ArgumentException("The document number is required.", nameof(request));

        return Task.Run(() => ImportCoreAsync(request, ct), ct);
    }

    public Task<Result> CheckAsync(string sourcePath, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(sourcePath))
            throw new ArgumentException($"The source must be an absolute path: '{sourcePath}'.", nameof(sourcePath));

        return Task.Run(async () =>
        {
            var pdf = await ReadPdfAsync(sourcePath, ct).ConfigureAwait(false);
            if (!pdf.IsSuccess)
                return Result.Fail(pdf.Error.Value, pdf.Detail);

            await using var uow = uowFactory.Create();
            return await FindArchivedAsync(uow, pdf.Value.Sha256, ct).ConfigureAwait(false) ?? Result.Ok();
        }, ct);
    }

    private async Task<Result<ImportResult>> ImportCoreAsync(ImportRequest request, CancellationToken ct)
    {
        // The archive gets exactly these bytes: the ones checked and hashed here.
        var pdf = await ReadPdfAsync(request.SourcePath, ct).ConfigureAwait(false);
        if (!pdf.IsSuccess)
            return Fail(pdf.Error.Value, pdf.Detail);
        var (content, sha256) = pdf.Value;

        await using var uow = uowFactory.Create();

        var root = await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
        if (!root.IsSuccess)
            return Fail(root.Error.Value, root.Detail);
        var archiveRoot = root.Value;

        var archived = await FindArchivedAsync(uow, sha256, ct).ConfigureAwait(false);
        if (archived is not null && !archived.IsSuccess)
            return Fail(archived.Error.Value, archived.Detail);

        var partner = await partnerResolver.GetOrCreateAsync(uow, request.PartnerName, ct).ConfigureAwait(false);
        if (!partner.IsSuccess)
            return Fail(partner.Error.Value, partner.Detail);

        var docNumber = request.DocNumber.Trim();
        var docNumberNorm = TextNormalizer.NormalizeNumber(docNumber);
        if (!request.ConfirmDuplicate &&
            await uow.Documents.NumberExistsAsync(partner.Value.Id, docNumberNorm, null, ct).ConfigureAwait(false))
            return Fail(ErrorCode.DuplicateNumber, docNumber);

        var now = clock.UtcNow;
        var document = new Document
        {
            Sha256 = sha256,
            Partner = partner.Value,
            DocNumber = docNumber,
            DocNumberNorm = docNumberNorm,
            IssueDate = request.IssueDate,
            Type = request.Type,
            Direction = request.Direction,
            OriginalName = Path.GetFileName(request.SourcePath),
            FileSizeBytes = content.Length,
            ImportedAt = now,
            UpdatedAt = now,
        };

        var moved = await archiveMover.ImportAsync(uow, document, request.SourcePath, content, archiveRoot, ct)
            .ConfigureAwait(false);
        if (!moved.IsSuccess)
            return Fail(moved.Error.Value, moved.Detail);

        // The import is committed, so this step is no longer cancellable.
        var sourceDeleted = !request.KeepSource
            && await storage.TryDeleteAsync(request.SourcePath, CancellationToken.None).ConfigureAwait(false);

        return Result.Ok(new ImportResult(document.ToListItem(), sourceDeleted));
    }

    // The whole file, checked to be a non-empty PDF, with its SHA-256.
    private async Task<Result<PdfFile>> ReadPdfAsync(string path, CancellationToken ct)
    {
        var read = await storage.ReadAsync(path, ct).ConfigureAwait(false);
        if (!read.IsSuccess)
            return Result.Fail<PdfFile>(read.Error.Value, read.Detail);

        var content = read.Value;
        if (content.Length == 0)
            return Result.Fail<PdfFile>(ErrorCode.Empty, path);
        if (!PdfSignature.IsPdf(content))
            return Result.Fail<PdfFile>(ErrorCode.NotPdf, path);
        return Result.Ok(new PdfFile(content, Sha256Hasher.Compute(content)));
    }

    // Deleted documents count too: a file already in _Papelera is restored, not imported again.
    // Null when the file isn't archived yet.
    private static async Task<Result?> FindArchivedAsync(IUnitOfWork uow, string sha256, CancellationToken ct)
    {
        var existing = await uow.Documents.GetByHashAsync(sha256, ct).ConfigureAwait(false);
        if (existing is null)
            return null;

        var error = existing.DeletedAt is null ? ErrorCode.AlreadyArchived : ErrorCode.AlreadyInTrash;
        return Result.Fail(error, existing.Id.ToString(CultureInfo.InvariantCulture));
    }

    private static Result<ImportResult> Fail(ErrorCode error, string? detail) =>
        Result.Fail<ImportResult>(error, detail);

    private sealed record PdfFile(byte[] Content, string Sha256);
}
