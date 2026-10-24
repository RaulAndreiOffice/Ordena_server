using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Ordena.Backend.Data;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

internal sealed class MaintenanceService(
    IAppPaths paths,
    DatabaseMigrator migrator,
    IUnitOfWorkFactory uowFactory,
    IFileStorage storage,
    SqliteBackup backup,
    JournalRecovery recovery,
    ArchiveCheck archiveCheck,
    FileRegistration registration,
    IClock clock,
    ILogger<MaintenanceService> logger) : IMaintenanceService
{
    public Task RunStartupAsync(CancellationToken ct) => Task.Run(async () =>
    {
        Directory.CreateDirectory(paths.Root);
        storage.TryEmptyFolder(paths.Preview);

        await BackupBeforeMigrationAsync(ct).ConfigureAwait(false);
        await migrator.MigrateAsync(ct).ConfigureAwait(false);

        var root = await ReadArchiveRootAsync(ct).ConfigureAwait(false);
        if (root is not null && storage.DirectoryExists(root))
            await recovery.RecoverAsync(root, ct).ConfigureAwait(false);
        else if (root is not null)
            logger.LogWarning("The archive {Root} is not available; interrupted moves wait until it is", root);
    }, ct);

    public Task<ArchiveStatus> GetArchiveStatusAsync(CancellationToken ct) => Task.Run(async () =>
    {
        var root = await ReadArchiveRootAsync(ct).ConfigureAwait(false);
        if (root is null)
            return ArchiveStatus.NotSet;
        return storage.DirectoryExists(root) ? ArchiveStatus.Available : ArchiveStatus.Unavailable;
    }, ct);

    public Task<Result<int>> RecoverInterruptedMovesAsync(CancellationToken ct) => Task.Run(async () =>
    {
        var root = await GetArchiveRootAsync(ct).ConfigureAwait(false);
        return root.IsSuccess
            ? Result.Ok(await recovery.RecoverAsync(root.Value, ct).ConfigureAwait(false))
            : Result.Fail<int>(root.Error.Value, root.Detail);
    }, ct);

    public Task WarmUpAsync(CancellationToken ct) => Task.Run(async () =>
    {
        // The same query shapes as the empty search, a one-word search and the partner autocomplete.
        await using var uow = uowFactory.Create();
        var oneWord = new DocumentSearch { Terms = [new DocumentSearch.Term("a", "A", null)] };
        await uow.Documents.SearchAsync(new DocumentSearch(), 1, ct).ConfigureAwait(false);
        await uow.Documents.CountAsync(new DocumentSearch(), ct).ConfigureAwait(false);
        await uow.Documents.SearchAsync(oneWord, 1, ct).ConfigureAwait(false);
        await uow.Partners.SuggestAsync("a", 1, ct).ConfigureAwait(false);
    }, ct);

    public Task<Result> BackupNowAsync(CancellationToken ct) =>
        Task.Run(() => BackupAsync(onlyIfChanged: false, ct), ct);

    public Task<Result> BackupIfChangedAsync(CancellationToken ct) =>
        Task.Run(() => BackupAsync(onlyIfChanged: true, ct), ct);

    public Task<Result<ArchiveCheckReport>> CheckArchiveAsync(CancellationToken ct) => Task.Run(async () =>
    {
        var root = await GetArchiveRootAsync(ct).ConfigureAwait(false);
        return root.IsSuccess
            ? Result.Ok(await archiveCheck.RunAsync(root.Value, ct).ConfigureAwait(false))
            : Result.Fail<ArchiveCheckReport>(root.Error.Value, root.Detail);
    }, ct);

    public Task<Result<RegisterReport>> RegisterFilesAsync(IReadOnlyList<string> paths, CancellationToken ct) => Task.Run(async () =>
    {
        var root = await GetArchiveRootAsync(ct).ConfigureAwait(false);
        return root.IsSuccess
            ? Result.Ok(await registration.RunAsync(root.Value, paths, ct).ConfigureAwait(false))
            : Result.Fail<RegisterReport>(root.Error.Value, root.Detail);
    }, ct);

    private async Task<Result> BackupAsync(bool onlyIfChanged, CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        var settings = await uow.Settings.GetAllAsync(ct).ConfigureAwait(false);

        // Before first run there is no archive, and no documents to lose.
        if (onlyIfChanged && settings.GetValueOrDefault(SettingKeys.ArchiveRoot) is null)
            return Result.Ok();

        var root = await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
        if (!root.IsSuccess)
            return Result.Fail(root.Error.Value, root.Detail);

        if (onlyIfChanged)
        {
            var lastUpdate = await uow.Documents.GetLastUpdateAsync(ct).ConfigureAwait(false);
            DateTime? lastBackup = settings.TryGetValue(SettingKeys.LastBackup, out var text)
                ? DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : null;
            if (lastUpdate is null || lastUpdate <= lastBackup)
                return Result.Ok();
        }

        var now = clock.UtcNow;
        try
        {
            var file = backup.BackupTo(Path.Combine(root.Value, SqliteBackup.ArchiveFolderName), now.ToLocalTime());
            logger.LogInformation("Backed up the database to {File}", file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            logger.LogWarning(ex, "The database backup into {Root} failed", root.Value);
            return Result.Fail(ErrorCode.IoError, ex.Message);
        }

        await uow.Settings.SetAsync(SettingKeys.LastBackup, now.ToString("O", CultureInfo.InvariantCulture), ct)
            .ConfigureAwait(false);
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Ok();
    }

    // A migration changes the schema, so the database is copied first: into the archive's _backup
    // when the archive is there, otherwise into the local backup folder next to the database.
    private async Task BackupBeforeMigrationAsync(CancellationToken ct)
    {
        if (!storage.FileExists(paths.Database) || !await migrator.HasPendingMigrationsAsync(ct).ConfigureAwait(false))
            return;

        string? root;
        try
        {
            root = await ReadArchiveRootAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            root = null; // an old schema the current model can't read
        }

        var folder = root is not null && storage.DirectoryExists(root)
            ? Path.Combine(root, SqliteBackup.ArchiveFolderName)
            : Path.Combine(paths.Root, SqliteBackup.LocalFolderName);
        var file = backup.BackupTo(folder, clock.UtcNow.ToLocalTime());
        logger.LogInformation("Backed up the database to {File} before migrating it", file);
    }

    private async Task<string?> ReadArchiveRootAsync(CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        var settings = await uow.Settings.GetAllAsync(ct).ConfigureAwait(false);
        return settings.GetValueOrDefault(SettingKeys.ArchiveRoot);
    }

    private async Task<Result<string>> GetArchiveRootAsync(CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        return await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
    }
}
