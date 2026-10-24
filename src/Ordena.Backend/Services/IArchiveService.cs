using Ordena.Backend.Dtos;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

// Where the archive is, and where the index comes from: Ajustes → "Archivo", and first run's
// "Ya tengo un archivo de facturas". The database stores only paths relative to the archive,
// so moving the archive only ever changes one setting. A relative path throws ArgumentException.
public interface IArchiveService
{
    // "Cambiar ubicación", after the user moved the folder: checks that a sample of the documents' files
    // is there, then uses it. ArchiveUnavailable when the folder doesn't exist, ArchiveMismatch when it
    // doesn't hold this archive (Detail is "found X of Y"). Interrupted moves are then resolved there.
    Task<Result> ChangeRootAsync(string newRoot, CancellationToken ct);

    // "Mover carpeta de facturas": backs up the database into the archive, copies every file to newRoot
    // checking each one, then switches to it. The old folder is left as it was. Also fails with
    // ArchiveUnavailable, InvalidArchiveFolder (newRoot is the archive, inside it or around it),
    // TargetExists (newRoot has files), NotEnoughSpace, and FileLocked or IoError for a file that
    // couldn't be copied. After a failure or a cancel nothing has changed and newRoot is empty again.
    Task<Result<ArchiveMoveReport>> MoveAsync(string newRoot, IProgress<ArchiveProgress>? progress, CancellationToken ct);

    // After a move, if the user agrees: deletes from oldRoot every file that has an identical copy
    // (same path and size) in the archive, then the folders left empty. Returns how many files it kept
    // because they had no copy. InvalidArchiveFolder when oldRoot is the archive, inside it or around it.
    Task<Result<int>> DeleteOldCopyAsync(string oldRoot, CancellationToken ct);

    // "Ya tengo un archivo": replaces the database with the newest usable backup in
    // <archiveRoot>/_backup and uses archiveRoot from now on. A database that already has documents is
    // backed up locally first, so nothing is lost. ArchiveUnavailable, or NoBackup (then RebuildIndexAsync).
    Task<Result<RestoreReport>> RestoreFromBackupAsync(string archiveRoot, CancellationToken ct);

    // "Reconstruir índice", when there is no backup: indexes every PDF whose path follows the app's
    // naming, reading each file once for its SHA-256, and uses archiveRoot from now on, unless it found
    // none (DocumentCount 0: not the invoices folder, so the setting stays as it was). The database
    // must be empty (InvalidOperationException otherwise). ArchiveUnavailable when the folder is missing.
    // A cancel during the reading leaves the database empty.
    Task<Result<RebuildReport>> RebuildIndexAsync(
        string archiveRoot, IProgress<ArchiveProgress>? progress, CancellationToken ct);
}
