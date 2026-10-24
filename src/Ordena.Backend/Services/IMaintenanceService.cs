using Ordena.Backend.Dtos;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

public interface IMaintenanceService
{
    // Called once at app startup, before anything else touches the database:
    // - empties the preview folder (copies left by the last session)
    // - backs up the database when an app update brings a migration, then creates or migrates it
    // - finishes or undoes file moves a crash interrupted, when the archive folder is available
    Task RunStartupAsync(CancellationToken ct);

    // NotSet before first run, Unavailable when the folder is gone (an unplugged disk), otherwise Available.
    Task<ArchiveStatus> GetArchiveStatusAsync(CancellationToken ct);

    // The journal step of RunStartupAsync, for when the archive comes back later ("Reintentar").
    // Returns how many interrupted moves it resolved; ArchiveUnavailable when the archive isn't there.
    Task<Result<int>> RecoverInterruptedMovesAsync(CancellationToken ct);

    // Runs the main queries once, so the first search the user types is as fast as the rest.
    // Call it in the background once the main window shows.
    Task WarmUpAsync(CancellationToken ct);

    // "Copia de seguridad ahora": the database into <archive>/_backup, keeping the newest 10.
    // ArchiveUnavailable, or IoError when the copy couldn't be written.
    Task<Result> BackupNowAsync(CancellationToken ct);

    // On exit: the same, but only when documents changed since the last backup. Ok when there was nothing to do.
    Task<Result> BackupIfChangedAsync(CancellationToken ct);

    // "Comprobar carpeta": compares the archive with the database. Files moved or renamed by hand are put back
    // where they belong; documents whose file is nowhere and PDFs no document points to are listed.
    // ArchiveUnavailable when the archive isn't there.
    Task<Result<ArchiveCheckReport>> CheckArchiveAsync(CancellationToken ct);

    // "Añadir a Ordena" for the check's unknown PDFs (full paths): those whose path follows the app's naming
    // are registered where they are; the rest come back in Remaining, for the manual import queue.
    // ArchiveUnavailable when the archive isn't there.
    Task<Result<RegisterReport>> RegisterFilesAsync(IReadOnlyList<string> paths, CancellationToken ct);
}
