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

    private async Task<Result<ImportResult>> ImportCoreAsync(ImportRequest request, CancellationToken ct)
    {
        // The archive gets exactly these bytes: the ones checked and hashed here.
        var read = await storage.ReadAsync(request.SourcePath, ct).ConfigureAwait(false);
        if (!read.IsSuccess)
            return Fail(read.Error.Value, read.Detail);

        var content = read.Value;
        if (content.Length == 0)
            return Fail(ErrorCode.Empty, request.SourcePath);
        if (!PdfSignature.IsPdf(content))
            return Fail(ErrorCode.NotPdf, request.SourcePath);
        var sha256 = Sha256Hasher.Compute(content);

        await using var uow = uowFactory.Create();

        var settings = await uow.Settings.GetAllAsync(ct).ConfigureAwait(false);
        var archiveRoot = settings.GetValueOrDefault(SettingKeys.ArchiveRoot)
            ?? throw new InvalidOperationException("The archive root is not set. First run sets it before any import.");
        if (!storage.DirectoryExists(archiveRoot))
            return Fail(ErrorCode.ArchiveUnavailable, archiveRoot);

        // Deleted documents count too: a file already in _Papelera is restored, not imported again.
        var existing = await uow.Documents.GetByHashAsync(sha256, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var error = existing.DeletedAt is null ? ErrorCode.AlreadyArchived : ErrorCode.AlreadyInTrash;
            return Fail(error, existing.Id.ToString(CultureInfo.InvariantCulture));
        }

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

        var archived = await archiveMover.ImportAsync(uow, document, request.SourcePath, content, archiveRoot, ct)
            .ConfigureAwait(false);
        if (!archived.IsSuccess)
            return Fail(archived.Error.Value, archived.Detail);

        // The import is committed, so this step is no longer cancellable.
        var sourceDeleted = await storage.TryDeleteAsync(request.SourcePath, CancellationToken.None).ConfigureAwait(false);

        var item = new DocumentListItem(
            document.Id, document.IssueDate, partner.Value.Name, document.DocNumber,
            document.Type, document.Direction, document.RelativePath);
        return Result.Ok(new ImportResult(item, sourceDeleted));
    }

    private static Result<ImportResult> Fail(ErrorCode error, string? detail) =>
        Result.Fail<ImportResult>(error, detail);
}
