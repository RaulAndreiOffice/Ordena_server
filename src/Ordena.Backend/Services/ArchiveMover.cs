using Microsoft.Extensions.Logging;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

// Every file move in the archive goes through the FileOperations journal:
// 1. save a Pending row, 2. write or move the file, 3. save the result and mark the row Done.
// If the app dies between 1 and 3, startup recovery finds the Pending row and checks where the file is:
// - an import (absolute FromPath) rolls forward: its document row is already complete, so a file
//   at ToPath means Done; no file means delete "<ToPath>.tmp" and undo the import.
// - a move (relative FromPath) rolls back: its changes are saved only in step 3, so a file
//   at ToPath is moved back to FromPath and the row is marked Failed.
// Shared by import, bulk import, edit, delete and restore.
internal sealed class ArchiveMover(IFileStorage storage, IClock clock, ILogger<ArchiveMover> logger)
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
            uow, archiveRoot, ArchiveNaming.BuildRelativePath(document, document.Partner), ownPath: null, ct)
            .ConfigureAwait(false);
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
            logger.LogWarning("Import of {Source} failed: {Error} {Detail}", sourcePath, written.Error, written.Detail);
            return written;
        }

        operation.State = FileOperationState.Done;
        await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        logger.LogInformation("Imported {Source} as {RelativePath}", sourcePath, relativePath);
        return Result.Ok();
    }

    // Moves a saved document's file somewhere else in the archive: a rename after an edit,
    // another folder, the trash or back from it. The caller must not change the document itself:
    // applyChanges runs once the file has moved, in the same save as the new path, so the row
    // never describes a file that isn't on disk. A failure marks the journal row Failed and
    // leaves the document as it was.
    public async Task<Result> MoveAsync(
        IUnitOfWork uow,
        Document document,
        string newRelativePath,
        string archiveRoot,
        Action<Document> applyChanges,
        CancellationToken ct)
    {
        var fromPath = document.RelativePath;
        var toPath = await FreeRelativePathAsync(uow, archiveRoot, newRelativePath, ownPath: fromPath, ct)
            .ConfigureAwait(false);

        // The name doesn't change (or the document keeps its _2), so only the row does.
        if (toPath == fromPath)
        {
            applyChanges(document);
            await uow.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }

        var operation = new FileOperation
        {
            Document = document,
            FromPath = fromPath,
            ToPath = toPath,
            State = FileOperationState.Pending,
            CreatedAt = clock.UtcNow,
        };
        uow.FileOperations.Add(operation);
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);

        // The move is in the journal now, so it runs to the end even if the caller cancels.
        var fromFullPath = ArchiveNaming.ToFullPath(archiveRoot, fromPath);
        var moved = await storage.MoveAsync(
            fromFullPath, ArchiveNaming.ToFullPath(archiveRoot, toPath), CancellationToken.None).ConfigureAwait(false);

        if (!moved.IsSuccess)
        {
            operation.State = FileOperationState.Failed;
            operation.Error = $"{moved.Error}: {moved.Detail}";
            await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            logger.LogWarning("Move of {FromPath} to {ToPath} failed: {Error}", fromPath, toPath, operation.Error);
            return moved;
        }

        applyChanges(document);
        document.RelativePath = toPath;
        operation.State = FileOperationState.Done;
        await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        logger.LogInformation("Moved {FromPath} to {ToPath}", fromPath, toPath);

        // A partner or quarter folder the file was the last one in.
        storage.TryDeleteEmptyFolders(Path.GetDirectoryName(fromFullPath)!, archiveRoot);
        return Result.Ok();
    }

    // Taken means a file on disk (even one the app doesn't know) or a row in the database
    // (a document whose file has gone missing still owns its path in the unique index).
    // ownPath, the moving document's current path, counts as free, even with a different case.
    private async Task<string> FreeRelativePathAsync(
        IUnitOfWork uow, string archiveRoot, string relativePath, string? ownPath, CancellationToken ct)
    {
        var candidate = relativePath;
        for (var n = 2; await IsTakenAsync(candidate).ConfigureAwait(false); n++)
            candidate = ArchiveNaming.WithCopyNumber(relativePath, n);
        return candidate;

        async Task<bool> IsTakenAsync(string path) =>
            !string.Equals(path, ownPath, StringComparison.OrdinalIgnoreCase)
            && (storage.FileExists(ArchiveNaming.ToFullPath(archiveRoot, path))
                || await uow.Documents.RelativePathExistsAsync(path, ct).ConfigureAwait(false));
    }
}
