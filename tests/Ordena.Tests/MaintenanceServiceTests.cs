using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests;

// Startup, backups and recovery as the app runs them: AddBackend on a real database file,
// a new service provider for each "start", and a fixed clock for predictable backup names.
public sealed class MaintenanceServiceTests : IDisposable
{
    private const string MarchPath = "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf";
    private const string AprilPath = "2026/2T/Recibidas/Telefonica-Espana/2026-04-01_Telefonica-Espana_A-2026-0123.pdf";

    private readonly TempFolder _appData = new();
    private readonly TempFolder _archive = new();
    private readonly TempFolder _downloads = new();
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 22, 8, 30, 0, DateTimeKind.Utc));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        // Pooled connections keep ordena.db open, which would block deleting the folder.
        SqliteConnection.ClearAllPools();
        _appData.Dispose();
        _archive.Dispose();
        _downloads.Dispose();
    }

    [Fact]
    public async Task The_archive_status_follows_the_setting_and_the_folder()
    {
        await using var provider = await StartAsync(setArchiveRoot: false);
        var maintenance = provider.GetRequiredService<IMaintenanceService>();

        Assert.Equal(ArchiveStatus.NotSet, await maintenance.GetArchiveStatusAsync(Ct));
        await provider.GetRequiredService<ISettingsService>().SetArchiveRootAsync(_archive.Root, Ct);
        Assert.Equal(ArchiveStatus.Available, await maintenance.GetArchiveStatusAsync(Ct));
        Directory.Delete(_archive.Root);
        Assert.Equal(ArchiveStatus.Unavailable, await maintenance.GetArchiveStatusAsync(Ct));
    }

    [Fact]
    public async Task Startup_empties_the_preview_folder()
    {
        _appData.WriteFile("preview/copy-1.pdf", [1]);
        _appData.WriteFile("preview/sub/copy-2.pdf", [1]);

        await using var provider = await StartAsync();

        Assert.True(Directory.Exists(_appData.Path("preview")));
        Assert.Empty(Directory.GetFileSystemEntries(_appData.Path("preview")));
    }

    // Plan step 5's "done when": killing the app in the middle of a move and restarting it
    // leaves the disk and the database consistent.
    [Fact]
    public async Task An_edit_killed_halfway_is_rolled_back_at_the_next_start()
    {
        var crashing = new CrashingStorage(new FileStorage(TimeSpan.FromMilliseconds(1)));
        int id;
        await using (var first = await StartAsync(crashing))
        {
            id = await ImportAsync(first);
            crashing.When = CrashingStorage.Crash.AfterMove;
            await Assert.ThrowsAsync<SimulatedCrashException>(() =>
                first.GetRequiredService<IDocumentService>().EditAsync(EditToApril(id), Ct));
        }
        Assert.Equal([AprilPath], _archive.Files());

        await using var second = await StartAsync();

        Assert.Equal([MarchPath], _archive.Files());
        var path = await second.GetRequiredService<IDocumentService>().GetFullPathAsync(id, Ct);
        Assert.Equal(_archive.Path(MarchPath.Split('/')), path.Value);
    }

    [Fact]
    public async Task Interrupted_moves_wait_until_the_archive_is_back()
    {
        var crashing = new CrashingStorage(new FileStorage(TimeSpan.FromMilliseconds(1)));
        await using (var first = await StartAsync(crashing))
        {
            var id = await ImportAsync(first);
            crashing.When = CrashingStorage.Crash.AfterMove;
            await Assert.ThrowsAsync<SimulatedCrashException>(() =>
                first.GetRequiredService<IDocumentService>().EditAsync(EditToApril(id), Ct));
        }
        var away = _archive.Root + "-unplugged";
        Directory.Move(_archive.Root, away);

        // The root is already saved; setting it again would recreate the unplugged folder.
        await using var second = await StartAsync(setArchiveRoot: false); // doesn't fail while the archive is away
        var maintenance = second.GetRequiredService<IMaintenanceService>();
        Assert.Equal(ErrorCode.ArchiveUnavailable, (await maintenance.RecoverInterruptedMovesAsync(Ct)).Error);

        Directory.Move(away, _archive.Root); // "Reintentar"
        Assert.Equal(1, (await maintenance.RecoverInterruptedMovesAsync(Ct)).Value);
        Assert.Equal([MarchPath], _archive.Files());
    }

    [Fact]
    public async Task BackupNow_writes_a_working_copy_into_the_archive()
    {
        await using var provider = await StartAsync();
        await ImportAsync(provider);

        var result = await provider.GetRequiredService<IMaintenanceService>().BackupNowAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([MarchPath, $"_backup/{BackupName(_clock.UtcNow)}"], _archive.Files());
        Assert.Equal(1L, CountDocumentsIn(_archive.Path("_backup", BackupName(_clock.UtcNow))));
        var settings = await provider.GetRequiredService<ISettingsService>().GetAsync(Ct);
        Assert.Equal(_clock.UtcNow, settings.LastBackupUtc);
    }

    // Another program (antivirus, OneDrive) holding the new file for a moment doesn't make the backup fail.
    [Fact]
    public async Task A_backup_waits_for_a_file_held_for_a_moment()
    {
        await using var provider = await StartAsync();
        await ImportAsync(provider);
        var held = _archive.Path("_backup", BackupName(_clock.UtcNow) + ".tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(held)!);
        var holder = new SqliteConnection($"Data Source={held};Pooling=False");
        holder.Open();
        using (var lockIt = holder.CreateCommand())
        {
            lockIt.CommandText = "CREATE TABLE t (x); BEGIN EXCLUSIVE; INSERT INTO t VALUES (1);";
            lockIt.ExecuteNonQuery();
        }
        var release = Task.Run(async () =>
        {
            await Task.Delay(250, Ct);
            holder.Dispose();
        }, Ct);

        var result = await provider.GetRequiredService<IMaintenanceService>().BackupNowAsync(Ct);
        await release;

        Assert.True(result.IsSuccess, $"{result.Error}: {result.Detail}");
        Assert.Equal(1L, CountDocumentsIn(_archive.Path("_backup", BackupName(_clock.UtcNow))));
    }

    [Fact]
    public async Task Only_the_newest_10_backups_are_kept()
    {
        await using var provider = await StartAsync();
        for (var day = 1; day <= 11; day++)
            _archive.WriteFile($"_backup/ordena-202609{day:00}-0800.db", [1]);

        await provider.GetRequiredService<IMaintenanceService>().BackupNowAsync(Ct);

        var backups = _archive.Files().Where(f => f.StartsWith("_backup/", StringComparison.Ordinal)).ToList();
        Assert.Equal(10, backups.Count);
        Assert.DoesNotContain("_backup/ordena-20260901-0800.db", backups);
        Assert.DoesNotContain("_backup/ordena-20260902-0800.db", backups);
        Assert.Contains($"_backup/{BackupName(_clock.UtcNow)}", backups);
    }

    [Fact]
    public async Task On_exit_a_backup_is_made_only_when_documents_changed()
    {
        await using var provider = await StartAsync(setArchiveRoot: false);
        var maintenance = provider.GetRequiredService<IMaintenanceService>();
        var settings = provider.GetRequiredService<ISettingsService>();

        Assert.True((await maintenance.BackupIfChangedAsync(Ct)).IsSuccess); // before first run: nothing to do
        await settings.SetArchiveRootAsync(_archive.Root, Ct);
        await maintenance.BackupIfChangedAsync(Ct); // no documents yet
        Assert.Null((await settings.GetAsync(Ct)).LastBackupUtc);

        var id = await ImportAsync(provider);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var backedUp = await maintenance.BackupIfChangedAsync(Ct);
        Assert.True(backedUp.IsSuccess, $"{backedUp.Error}: {backedUp.Detail}");
        var afterImport = _clock.UtcNow;
        Assert.Equal(afterImport, (await settings.GetAsync(Ct)).LastBackupUtc);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await maintenance.BackupIfChangedAsync(Ct); // nothing changed since
        Assert.Equal(afterImport, (await settings.GetAsync(Ct)).LastBackupUtc);

        await provider.GetRequiredService<IDocumentService>().DeleteAsync(id, Ct);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await maintenance.BackupIfChangedAsync(Ct);
        Assert.Equal(_clock.UtcNow, (await settings.GetAsync(Ct)).LastBackupUtc);
    }

    [Fact]
    public async Task A_backup_needs_the_archive()
    {
        await using var provider = await StartAsync();
        Directory.Delete(_archive.Root);

        var result = await provider.GetRequiredService<IMaintenanceService>().BackupNowAsync(Ct);

        Assert.Equal(ErrorCode.ArchiveUnavailable, result.Error);
    }

    [Fact]
    public async Task A_database_with_a_pending_migration_is_backed_up_before_migrating()
    {
        // A database file the migrations haven't touched yet, like one from an older app version.
        using (var connection = new SqliteConnection($"Data Source={_appData.Path("ordena.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE OldData (Id INTEGER)";
            command.ExecuteNonQuery();
        }

        await using var provider = await StartAsync(setArchiveRoot: false);

        // No readable archive setting in the old schema, so the copy goes next to the database.
        var backup = Assert.Single(Directory.GetFiles(_appData.Path(SqliteBackup.LocalFolderName)));
        Assert.Equal(BackupName(_clock.UtcNow), Path.GetFileName(backup));
        Assert.True((await provider.GetRequiredService<ISearchService>().SearchAsync(new SearchQuery(), Ct)).Items is []);
    }

    [Fact]
    public async Task WarmUp_and_the_folder_check_run_on_a_new_archive()
    {
        await using var provider = await StartAsync();
        var maintenance = provider.GetRequiredService<IMaintenanceService>();

        await maintenance.WarmUpAsync(Ct);
        var report = await maintenance.CheckArchiveAsync(Ct);

        Assert.Equal(0, report.Value.DocumentCount);
        Assert.Empty(report.Value.UnknownFiles);
    }

    // One "start" of the app: build the container, run the startup step.
    private async Task<ServiceProvider> StartAsync(IFileStorage? storage = null, bool setArchiveRoot = true)
    {
        var services = new ServiceCollection().AddBackend(_appData.Root);
        services.AddSingleton<IClock>(_clock);
        if (storage is not null)
            services.AddSingleton(storage);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await provider.GetRequiredService<IMaintenanceService>().RunStartupAsync(Ct);
        if (setArchiveRoot)
            await provider.GetRequiredService<ISettingsService>().SetArchiveRootAsync(_archive.Root, Ct);
        return provider;
    }

    private async Task<int> ImportAsync(IServiceProvider provider)
    {
        var result = await provider.GetRequiredService<IImportService>().ImportAsync(new ImportRequest
        {
            SourcePath = _downloads.WriteFile("a.pdf", TestPdf.Bytes("a")),
            PartnerName = "Telefónica España, S.A.U.",
            DocNumber = "A-2026/0123",
            IssueDate = new DateOnly(2026, 3, 14),
            Type = DocType.Factura,
            Direction = Direction.Recibida,
        }, Ct);
        return result.Value.Document.Id;
    }

    private static EditDocumentRequest EditToApril(int id) => new()
    {
        Id = id,
        PartnerName = "Telefónica España, S.A.U.",
        DocNumber = "A-2026/0123",
        IssueDate = new DateOnly(2026, 4, 1),
        Type = DocType.Factura,
        Direction = Direction.Recibida,
        IsPaid = false,
    };

    // Named in local time, like the clock on the wall.
    private static string BackupName(DateTime utc) =>
        string.Create(CultureInfo.InvariantCulture, $"ordena-{utc.ToLocalTime():yyyyMMdd-HHmm}.db");

    private static long CountDocumentsIn(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Documents";
        return (long)command.ExecuteScalar()!;
    }
}
