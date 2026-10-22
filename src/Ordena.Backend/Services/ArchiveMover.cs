using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

// Every file move in the archive goes through the FileOperations journal:
// 1. save a Pending row, 2. write or move the file, 3. save the result and mark the row Done.
// If the app dies between 1 and 3, startup recovery finds the Pending row and checks where the file is.
// Shared by import and bulk import, and later by edit, delete and restore.
internal sealed class ArchiveMover(IFileStorage storage, IClock clock)
{
    // Adds a new document and writes its file into the archive under the standard name,
    // with _2, _3… when that name is taken. The document and a new partner are saved here.
    // A failure leaves nothing behind: no file, no document, no journal row, no new partner.
    public async Task<Result> ImportAsync(
        IUnitOfWork uow,
        Document document,
        string sourcePath,
        ReadOnlyMemory<byte> content,
        string archiveRoot,
        CancellationToken ct)
    {
        var relativePath = await FreeRelativePathAsync(
            uow, archiveRoot, ArchiveNaming.BuildRelativePath(document, document.Partner), ct).ConfigureAwait(false);
        var partnerIsNew = document.Partner.Id == 0;

        document.RelativePath = relativePath;
        uow.Documents.Add(document);
        var operation = new FileOperation
        {
            Document = document,
            FromPath = sourcePath,
            ToPath = relativePath,
            State = FileOperationState.Pending,
            CreatedAt = clock.UtcNow,
        };
        uow.FileOperations.Add(operation);
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);

        // The move is in the journal now, so it runs to the end even if the caller cancels.
        var written = await storage.WriteVerifiedAsync(
            ArchiveNaming.ToFullPath(archiveRoot, relativePath), content, document.Sha256, CancellationToken.None)
            .ConfigureAwait(false);

        if (!written.IsSuccess)
        {
            uow.FileOperations.Remove(operation);
            uow.Documents.Remove(document);
            if (partnerIsNew)
                uow.Partners.Remove(document.Partner);
            await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return written;
        }

        operation.State = FileOperationState.Done;
        await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return Result.Ok();
    }

    // Taken means a file on disk (even one the app doesn't know) or a row in the database
    // (a document whose file has gone missing still owns its path in the unique index).
    private async Task<string> FreeRelativePathAsync(
        IUnitOfWork uow, string archiveRoot, string relativePath, CancellationToken ct)
    {
        var candidate = relativePath;
        for (var n = 2; await IsTakenAsync(candidate).ConfigureAwait(false); n++)
            candidate = ArchiveNaming.WithCopyNumber(relativePath, n);
        return candidate;

        async Task<bool> IsTakenAsync(string path) =>
            storage.FileExists(ArchiveNaming.ToFullPath(archiveRoot, path))
            || await uow.Documents.RelativePathExistsAsync(path, ct).ConfigureAwait(false);
    }
}
