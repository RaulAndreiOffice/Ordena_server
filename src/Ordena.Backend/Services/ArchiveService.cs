using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Ordena.Backend.Data;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

internal sealed class ArchiveService(
    IAppPaths paths,
    DatabaseMigrator migrator,
    IUnitOfWorkFactory uowFactory,
    IFileStorage storage,
    SqliteBackup backup,
    JournalRecovery recovery,
    IndexRebuild indexRebuild,
    IClock clock,
    ILogger<ArchiveService> logger) : IArchiveService
{
    // Enough to tell the right folder (nearly every file found) from a wrong one (nearly none).
    private const int SampleSize = 20;

    public Task<Result> ChangeRootAsync(string newRoot, CancellationToken ct)
    {
        RequireAbsolute(newRoot, nameof(newRoot));
        return Task.Run(async () =>
        {
            if (!storage.DirectoryExists(newRoot))
                return Result.Fail(ErrorCode.ArchiveUnavailable, newRoot);

            await using (var uow = uowFactory.Create())
            {
                var documents = await uow.Documents.ListAllAsync(ct).ConfigureAwait(false);
                var step = Math.Max(1, documents.Count / SampleSize);
                var sample = documents.Where((_, i) => i % step == 0).Take(SampleSize).ToList();
                var found = sample.Count(d => storage.FileExists(ArchiveNaming.ToFullPath(newRoot, d.RelativePath)));

                // Half is enough: files that were already missing before the move mustn't block it.
                if (found * 2 < sample.Count)
                    return Result.Fail(ErrorCode.ArchiveMismatch, $"found {found} of {sample.Count}");

                await uow.Settings.SetAsync(SettingKeys.ArchiveRoot, newRoot, ct).ConfigureAwait(false);
                await uow.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            logger.LogInformation("The archive is now at {Root}", newRoot);
            await recovery.RecoverAsync(newRoot, ct).ConfigureAwait(false);
            return Result.Ok();
        }, ct);
    }

    public Task<Result<ArchiveMoveReport>> MoveAsync(
        string newRoot, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        RequireAbsolute(newRoot, nameof(newRoot));
        return Task.Run(() => MoveCoreAsync(newRoot, progress, ct), ct);
    }

    public Task<Result<int>> DeleteOldCopyAsync(string oldRoot, CancellationToken ct)
    {
        RequireAbsolute(oldRoot, nameof(oldRoot));
        return Task.Run(async () =>
        {
            var root = await GetRootAsync(ct).ConfigureAwait(false);
            if (!root.IsSuccess)
                return Result.Fail<int>(root.Error.Value, root.Detail);
            if (ArchiveNaming.AreNested(root.Value, oldRoot))
                return Result.Fail<int>(ErrorCode.InvalidArchiveFolder, oldRoot);

            // Empty folders go up to oldRoot itself; a whole disk ("E:\") is kept.
            var fullOldRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(oldRoot));
            var stopAt = Path.GetDirectoryName(fullOldRoot) ?? fullOldRoot;
            var kept = 0;
            foreach (var file in storage.ListFiles(oldRoot))
            {
                ct.ThrowIfCancellationRequested();
                var copy = ArchiveNaming.ToFullPath(root.Value, ArchiveNaming.ToRelativePath(oldRoot, file));
                var identical = storage.FileExists(copy) && storage.GetLength(copy) == storage.GetLength(file);
                if (identical && await storage.TryDeleteAsync(file, ct).ConfigureAwait(false))
                    storage.TryDeleteEmptyFolders(Path.GetDirectoryName(file)!, stopAt);
                else
                    kept++;
            }

            logger.LogInformation("Deleted the old archive copy at {OldRoot}; kept {Kept} files without a copy", oldRoot, kept);
            return Result.Ok(kept);
        }, ct);
    }

    public Task<Result<RestoreReport>> RestoreFromBackupAsync(string archiveRoot, CancellationToken ct)
    {
        RequireAbsolute(archiveRoot, nameof(archiveRoot));
        return Task.Run(async () =>
        {
            if (!storage.DirectoryExists(archiveRoot))
                return Result.Fail<RestoreReport>(ErrorCode.ArchiveUnavailable, archiveRoot);
            var file = backup.FindNewestUsable(Path.Combine(archiveRoot, SqliteBackup.ArchiveFolderName));
            if (file is null)
                return Result.Fail<RestoreReport>(ErrorCode.NoBackup, archiveRoot);

            // Nothing is ever lost: a database that already has documents is copied aside first.
            await using (var uow = uowFactory.Create())
            {
                if (await uow.Documents.GetLastUpdateAsync(ct).ConfigureAwait(false) is not null)
                {
                    var aside = backup.BackupTo(Path.Combine(paths.Root, SqliteBackup.LocalFolderName), clock.UtcNow.ToLocalTime());
                    logger.LogInformation("Kept the current database as {File} before restoring", aside);
                }
            }

            backup.RestoreFrom(file);
            await migrator.MigrateAsync(ct).ConfigureAwait(false); // the backup may come from an older version

            int count;
            await using (var uow = uowFactory.Create())
            {
                // The backup still names the folder of the PC it was made on.
                await uow.Settings.SetAsync(SettingKeys.ArchiveRoot, archiveRoot, ct).ConfigureAwait(false);
                await uow.SaveChangesAsync(ct).ConfigureAwait(false);
                count = await uow.Documents.CountAsync(new DocumentSearch(), ct).ConfigureAwait(false);
            }

            logger.LogInformation("Restored the database from {File}: {Count} documents", file, count);
            await recovery.RecoverAsync(archiveRoot, ct).ConfigureAwait(false);
            return Result.Ok(new RestoreReport(file, count));
        }, ct);
    }

    public Task<Result<RebuildReport>> RebuildIndexAsync(
        string archiveRoot, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        RequireAbsolute(archiveRoot, nameof(archiveRoot));
        return Task.Run(async () =>
        {
            if (!storage.DirectoryExists(archiveRoot))
                return Result.Fail<RebuildReport>(ErrorCode.ArchiveUnavailable, archiveRoot);

            var report = await indexRebuild.RunAsync(archiveRoot, progress, ct).ConfigureAwait(false);

            await using var uow = uowFactory.Create();
            await uow.Settings.SetAsync(SettingKeys.ArchiveRoot, archiveRoot, CancellationToken.None).ConfigureAwait(false);
            await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return Result.Ok(report);
        }, ct);
    }

    private async Task<Result<ArchiveMoveReport>> MoveCoreAsync(
        string newRoot, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        var root = await GetRootAsync(ct).ConfigureAwait(false);
        if (!root.IsSuccess)
            return Result.Fail<ArchiveMoveReport>(root.Error.Value, root.Detail);
        var oldRoot = root.Value;

        if (ArchiveNaming.AreNested(oldRoot, newRoot))
            return Result.Fail<ArchiveMoveReport>(ErrorCode.InvalidArchiveFolder, newRoot);
        if (storage.ListFiles(newRoot).Count > 0)
            return Result.Fail<ArchiveMoveReport>(ErrorCode.TargetExists, newRoot);

        // Moves a crash interrupted are settled in the old place first, or a half-moved file would be copied.
        await recovery.RecoverAsync(oldRoot, ct).ConfigureAwait(false);
        await using (var uow = uowFactory.Create())
        {
            var stillPending = await uow.FileOperations.GetPendingAsync(ct).ConfigureAwait(false);
            if (stillPending.Count > 0)
                return Result.Fail<ArchiveMoveReport>(ErrorCode.FileLocked, stillPending[0].ToPath);
        }

        // The freshest backup travels with the archive.
        try
        {
            backup.BackupTo(Path.Combine(oldRoot, SqliteBackup.ArchiveFolderName), clock.UtcNow.ToLocalTime());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return Result.Fail<ArchiveMoveReport>(ErrorCode.IoError, ex.Message);
        }

        var files = storage.ListFiles(oldRoot)
            .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: f, Length: storage.GetLength(f)))
            .ToList();
        var total = files.Sum(f => f.Length);
        var needed = total + total / 10;
        if (storage.AvailableFreeSpace(newRoot) is { } free && free < needed)
            return Result.Fail<ArchiveMoveReport>(ErrorCode.NotEnoughSpace, needed.ToString(CultureInfo.InvariantCulture));

        // Until the setting switches, any way out (a failed file, a cancel, a bug) removes the copies again.
        List<string> copies = [];
        var switched = false;
        try
        {
            var done = 0L;
            foreach (var (i, file) in files.Index())
            {
                ct.ThrowIfCancellationRequested();
                var target = ArchiveNaming.ToFullPath(newRoot, ArchiveNaming.ToRelativePath(oldRoot, file.Path));
                var copied = await CopyAsync(file.Path, target).ConfigureAwait(false);
                if (!copied.IsSuccess)
                {
                    logger.LogWarning("Moving the archive stopped at {File}: {Error} {Detail}", file.Path, copied.Error, copied.Detail);
                    return Result.Fail<ArchiveMoveReport>(copied.Error.Value, copied.Detail);
                }

                copies.Add(target);
                done += file.Length;
                progress?.Report(new ArchiveProgress(i + 1, files.Count, done, total));
            }

            await using (var uow = uowFactory.Create())
            {
                await uow.Settings.SetAsync(SettingKeys.ArchiveRoot, newRoot, CancellationToken.None).ConfigureAwait(false);
                await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            switched = true;
        }
        finally
        {
            if (!switched)
                await RemoveCopiesAsync(copies, newRoot).ConfigureAwait(false);
        }

        logger.LogInformation("Moved the archive from {OldRoot} to {NewRoot}: {Files} files, {Bytes} bytes",
            oldRoot, newRoot, files.Count, total);
        return Result.Ok(new ArchiveMoveReport(oldRoot, newRoot, files.Count, total));
    }

    // Read once, write those bytes, check them on disk: the copy is exactly the original.
    // A single file isn't cancelled halfway; the move checks for a cancel between files.
    private async Task<Result> CopyAsync(string source, string target)
    {
        var read = await storage.ReadAsync(source, CancellationToken.None).ConfigureAwait(false);
        if (!read.IsSuccess)
            return Result.Fail(read.Error.Value, read.Detail);

        return await storage.WriteVerifiedAsync(
            target, read.Value, Sha256Hasher.Compute(read.Value), CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RemoveCopiesAsync(IEnumerable<string> copies, string newRoot)
    {
        foreach (var copy in copies)
        {
            await storage.TryDeleteAsync(copy, CancellationToken.None).ConfigureAwait(false);
            storage.TryDeleteEmptyFolders(Path.GetDirectoryName(copy)!, newRoot);
        }
    }

    private async Task<Result<string>> GetRootAsync(CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        return await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
    }

    private static void RequireAbsolute(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException($"The folder must be an absolute path: '{path}'.", name);
    }
}
