using Microsoft.Extensions.Logging;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

// Resolves the file moves a crash left Pending, by checking where each file really is.
// The rules match how ArchiveMover writes the journal: an import rolls forward, a move rolls back.
internal sealed class JournalRecovery(
    IUnitOfWorkFactory uowFactory, IFileStorage storage, ILogger<JournalRecovery> logger)
{
    // Returns how many interrupted moves it resolved. One it can't resolve yet,
    // because a file is locked, stays Pending for the next try.
    public async Task<int> RecoverAsync(string archiveRoot, CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        var resolved = 0;

        foreach (var operation in await uow.FileOperations.GetPendingAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var done = Path.IsPathFullyQualified(operation.FromPath)
                    ? await RecoverImportAsync(uow, operation, archiveRoot, ct).ConfigureAwait(false)
                    : await RecoverMoveAsync(operation, archiveRoot, ct).ConfigureAwait(false);
                if (!done)
                    continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Interrupted move {Id} ({ToPath}) left for the next start", operation.Id, operation.ToPath);
                continue;
            }

            // One save per operation, so a problem with one can't hold up the others.
            await uow.SaveChangesAsync(ct).ConfigureAwait(false);
            resolved++;
        }

        return resolved;
    }

    // The document row was complete before the file was written. A file with its content at ToPath
    // means the import finished; anything else means it never did, and the original is still in place.
    private async Task<bool> RecoverImportAsync(
        IUnitOfWork uow, FileOperation operation, string archiveRoot, CancellationToken ct)
    {
        var document = await uow.Documents.GetByIdAsync(operation.DocumentId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Journal row {operation.Id} points to no document.");
        var target = ArchiveNaming.ToFullPath(archiveRoot, operation.ToPath);

        if (storage.FileExists(target) && Sha256Hasher.ComputeFile(target) == document.Sha256)
        {
            operation.State = FileOperationState.Done;
            logger.LogWarning("Interrupted import of {Source} completed: the file is at {Target}", operation.FromPath, operation.ToPath);
            return true;
        }

        await storage.TryDeleteAsync(target + ".tmp", ct).ConfigureAwait(false);
        uow.FileOperations.Remove(operation);
        uow.Documents.Remove(document);
        logger.LogWarning("Interrupted import of {Source} undone: the file never reached the archive", operation.FromPath);
        return true;
    }

    // A move saves the document's new values only once the file has moved, so the row still
    // describes FromPath: a file already at ToPath goes back there.
    private async Task<bool> RecoverMoveAsync(FileOperation operation, string archiveRoot, CancellationToken ct)
    {
        var from = ArchiveNaming.ToFullPath(archiveRoot, operation.FromPath);
        var to = ArchiveNaming.ToFullPath(archiveRoot, operation.ToPath);

        if (storage.FileExists(to) && !storage.FileExists(from))
        {
            var back = await storage.MoveAsync(to, from, ct).ConfigureAwait(false);
            if (!back.IsSuccess)
            {
                logger.LogWarning("Interrupted move {Id} could not be undone yet: {Error} {Detail}", operation.Id, back.Error, back.Detail);
                return false;
            }
            storage.TryDeleteEmptyFolders(Path.GetDirectoryName(to)!, archiveRoot);
            MarkFailed(operation, "Interrupted; the file was moved back.");
        }
        else if (storage.FileExists(from))
        {
            MarkFailed(operation, "Interrupted before the file moved.");
        }
        else
        {
            MarkFailed(operation, "Interrupted, and the file is in neither place.");
        }

        logger.LogWarning("Interrupted move of {FromPath} to {ToPath}: {Error}", operation.FromPath, operation.ToPath, operation.Error);
        return true;
    }

    private static void MarkFailed(FileOperation operation, string error)
    {
        operation.State = FileOperationState.Failed;
        operation.Error = error;
    }
}
