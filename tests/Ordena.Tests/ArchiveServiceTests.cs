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

// Moving the archive, restoring the database from its backups and rebuilding it from the file names,
// as the app does them: AddBackend on a real database file, one service provider per "start".
public sealed class ArchiveServiceTests : IDisposable
{
    private const string UnknownPdf = "2026/escaneo suelto.pdf";
    private const string TrashedPdf = "_Papelera/2026/4T/Recibidas/Gestoria-Lopez/2026-12-31_Gestoria-Lopez_R-15_Recibo.pdf";

    // Received and issued, every type, two with the same name (the second gets _2), one deleted.
    private static readonly (string Partner, string Number, string Date, DocType Type, Direction Direction)[] Invoices =
    [
        ("Telefónica España, S.A.U.", "A-2026/0123", "2026-03-14", DocType.Factura, Direction.Recibida),
        ("Repsol S.A.", "T-88812", "2026-03-20", DocType.FacturaSimplificada, Direction.Recibida),
        ("Iberdrola Clientes S.A.U.", "21/0042", "2026-04-02", DocType.FacturaRectificativa, Direction.Recibida),
        ("Cliente Ejemplo S.L.", "F-2026-007", "2026-07-03", DocType.Factura, Direction.Emitida),
        ("Cliente Ejemplo S.L.", "F-2026-007", "2026-07-03", DocType.Factura, Direction.Emitida),
        ("Gestoría López", "R-15", "2026-12-31", DocType.Recibo, Direction.Recibida),
        ("Mercadona S.A.", "P-1", "2025-11-05", DocType.Proforma, Direction.Emitida),
        ("Ayuntamiento de Valencia", "Tasa 2026", "2026-01-15", DocType.Otro, Direction.Recibida),
    ];

    private readonly TempFolder _appData = new();
    private readonly TempFolder _archive = new();
    private readonly TempFolder _elsewhere = new(); // another disk, a new folder
    private readonly TempFolder _downloads = new();
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 22, 8, 30, 0, DateTimeKind.Utc));
    private readonly CrashingStorage _storage = new(new FileStorage(TimeSpan.FromMilliseconds(1)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _appData.Dispose();
        _archive.Dispose();
        _elsewhere.Dispose();
        _downloads.Dispose();
    }

    [Fact]
    public async Task ChangeRoot_accepts_the_folder_the_archive_was_moved_to()
    {
        await using var provider = await StartAsync();
        var ids = await FillAsync(provider);
        var moved = _elsewhere.Path("Facturas");
        Directory.Move(_archive.Root, moved); // by hand, in Explorer

        var result = await provider.GetRequiredService<IArchiveService>().ChangeRootAsync(moved, Ct);

        Assert.True(result.IsSuccess);
        var path = await provider.GetRequiredService<IDocumentService>().GetFullPathAsync(ids[0], Ct);
        Assert.StartsWith(moved + Path.DirectorySeparatorChar, path.Value);
    }

    [Fact]
    public async Task ChangeRoot_refuses_a_folder_without_the_archives_files()
    {
        await using var provider = await StartAsync();
        await FillAsync(provider);

        var result = await provider.GetRequiredService<IArchiveService>().ChangeRootAsync(_elsewhere.Root, Ct);

        Assert.Equal(ErrorCode.ArchiveMismatch, result.Error);
        Assert.Equal("found 0 of 8", result.Detail);
        Assert.Equal(_archive.Root, await ArchiveRootSettingAsync(provider));
    }

    [Fact]
    public async Task ChangeRoot_to_a_missing_folder_is_ArchiveUnavailable()
    {
        await using var provider = await StartAsync();

        var result = await provider.GetRequiredService<IArchiveService>().ChangeRootAsync(_elsewhere.Path("nada"), Ct);

        Assert.Equal(ErrorCode.ArchiveUnavailable, result.Error);
    }

    [Fact]
    public async Task ChangeRoot_without_documents_just_switches()
    {
        await using var provider = await StartAsync();

        Assert.True((await provider.GetRequiredService<IArchiveService>().ChangeRootAsync(_elsewhere.Root, Ct)).IsSuccess);
        Assert.Equal(_elsewhere.Root, await ArchiveRootSettingAsync(provider));
    }

    [Fact]
    public async Task Move_copies_every_file_and_switches_to_the_new_folder()
    {
        await using var provider = await StartAsync();
        var ids = await FillAsync(provider);
        var target = _elsewhere.Path("Facturas");
        List<ArchiveProgress> reports = [];

        var result = await provider.GetRequiredService<IArchiveService>()
            .MoveAsync(target, new SyncProgress<ArchiveProgress>(reports.Add), Ct);

        Assert.True(result.IsSuccess);
        var files = _archive.Files(); // the old folder stays, with the backup made for the move
        Assert.Contains(files, f => f.StartsWith("_backup/ordena-", StringComparison.Ordinal));
        Assert.Contains(TrashedPdf, files);
        Assert.Contains(UnknownPdf, files);
        Assert.Equal(files, TempFolder.FilesUnder(target));
        foreach (var file in files)
            Assert.Equal(File.ReadAllBytes(_archive.Path(file)), File.ReadAllBytes(Path.Combine(target, file)));

        Assert.Equal(target, await ArchiveRootSettingAsync(provider));
        foreach (var id in ids)
            Assert.StartsWith(target + Path.DirectorySeparatorChar,
                (await provider.GetRequiredService<IDocumentService>().GetFullPathAsync(id, Ct)).Value);

        var last = reports[^1];
        Assert.Equal((files.Length, files.Length, last.BytesTotal), (last.FilesDone, last.FilesTotal, last.BytesDone));
        Assert.Equal(new ArchiveMoveReport(_archive.Root, target, files.Length, last.BytesTotal), result.Value);
    }

    [Fact]
    public async Task Move_into_a_folder_with_files_is_TargetExists()
    {
        await using var provider = await StartAsync();
        _elsewhere.WriteFile("Facturas/nota.txt", [1]);

        var result = await provider.GetRequiredService<IArchiveService>().MoveAsync(_elsewhere.Path("Facturas"), null, Ct);

        Assert.Equal(ErrorCode.TargetExists, result.Error);
    }

    [Fact]
    public async Task Move_into_the_archive_itself_is_InvalidArchiveFolder()
    {
        await using var provider = await StartAsync();

        var result = await provider.GetRequiredService<IArchiveService>().MoveAsync(_archive.Path("nuevo"), null, Ct);

        Assert.Equal(ErrorCode.InvalidArchiveFolder, result.Error);
    }

    [Fact]
    public async Task A_move_that_fails_halfway_leaves_everything_as_it_was()
    {
        await using var provider = await StartAsync();
        await FillAsync(provider);
        var target = _elsewhere.Path("Facturas");

        Result<ArchiveMoveReport> result;
        // The trash comes late in the copy, so other files are copied before it fails.
        using (File.Open(_archive.Path(TrashedPdf), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await provider.GetRequiredService<IArchiveService>().MoveAsync(target, null, Ct);

        Assert.Equal(ErrorCode.FileLocked, result.Error);
        Assert.Empty(TempFolder.FilesUnder(target));
        Assert.Equal(_archive.Root, await ArchiveRootSettingAsync(provider));
    }

    [Fact]
    public async Task A_cancelled_move_leaves_everything_as_it_was()
    {
        await using var provider = await StartAsync();
        await FillAsync(provider);
        var target = _elsewhere.Path("Facturas");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new SyncProgress<ArchiveProgress>(p =>
        {
            if (p.FilesDone == 2)
                cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<IArchiveService>().MoveAsync(target, progress, cts.Token));

        Assert.Empty(TempFolder.FilesUnder(target));
        Assert.Equal(_archive.Root, await ArchiveRootSettingAsync(provider));
    }

    [Fact]
    public async Task Move_without_room_for_the_archive_is_NotEnoughSpace()
    {
        await using var provider = await StartAsync();
        await FillAsync(provider);
        _storage.FreeSpace = 0;

        var result = await provider.GetRequiredService<IArchiveService>().MoveAsync(_elsewhere.Path("Facturas"), null, Ct);

        Assert.Equal(ErrorCode.NotEnoughSpace, result.Error);
        Assert.True(long.Parse(result.Detail!, CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task DeleteOldCopy_deletes_only_files_with_an_identical_copy()
    {
        await using var provider = await StartAsync();
        await FillAsync(provider);
        var archive = provider.GetRequiredService<IArchiveService>();
        var target = _elsewhere.Path("Facturas");
        await archive.MoveAsync(target, null, Ct);
        _archive.WriteFile("2026/añadido después.pdf", [1]); // only in the old folder

        var result = await archive.DeleteOldCopyAsync(_archive.Root, Ct);

        Assert.Equal(1, result.Value);
        Assert.Equal(["2026/añadido después.pdf"], _archive.Files());
        Assert.Contains(TrashedPdf, TempFolder.FilesUnder(target));
    }

    [Fact]
    public async Task DeleteOldCopy_of_the_archive_in_use_is_InvalidArchiveFolder()
    {
        await using var provider = await StartAsync();

        var result = await provider.GetRequiredService<IArchiveService>().DeleteOldCopyAsync(_archive.Root, Ct);

        Assert.Equal(ErrorCode.InvalidArchiveFolder, result.Error);
    }

    // Plan step 5b's "done when": a new PC, with the archive on another disk, gets every row back.
    [Fact]
    public async Task Restore_gives_back_the_same_rows_on_a_new_pc()
    {
        string[] before;
        int[] ids;
        await using (var oldPc = await StartAsync())
        {
            ids = await FillAsync(oldPc);
            await oldPc.GetRequiredService<IMaintenanceService>().BackupNowAsync(Ct);
            before = FullSnapshot();
        }
        DeleteDatabase();
        var newDisk = _elsewhere.Path("Facturas");
        Directory.Move(_archive.Root, newDisk);

        await using var newPc = await StartAsync(setArchiveRoot: false);
        var result = await newPc.GetRequiredService<IArchiveService>().RestoreFromBackupAsync(newDisk, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.DocumentCount); // the one in the trash isn't in search
        Assert.Equal(_clock.UtcNow.ToLocalTime(), result.Value.BackupTime); // 08:30 to the minute, as in the name
        Assert.Equal(before, FullSnapshot());
        Assert.Equal(newDisk, await ArchiveRootSettingAsync(newPc));
        Assert.True((await newPc.GetRequiredService<IDocumentService>().GetFullPathAsync(ids[0], Ct)).IsSuccess);
    }

    [Fact]
    public async Task Restore_skips_a_damaged_newest_backup()
    {
        await using var provider = await StartAsync();
        await FillAsync(provider);
        await provider.GetRequiredService<IMaintenanceService>().BackupNowAsync(Ct);
        var good = Assert.Single(Directory.GetFiles(_archive.Path("_backup")));
        _archive.WriteFile("_backup/ordena-29991231-2359.db", "not a database"u8.ToArray());

        var result = await provider.GetRequiredService<IArchiveService>().RestoreFromBackupAsync(_archive.Root, Ct);

        Assert.Equal(good, result.Value.BackupFile);
    }

    [Fact]
    public async Task Restore_without_a_backup_is_NoBackup()
    {
        await using var provider = await StartAsync(setArchiveRoot: false);

        var result = await provider.GetRequiredService<IArchiveService>().RestoreFromBackupAsync(_archive.Root, Ct);

        Assert.Equal(ErrorCode.NoBackup, result.Error);
    }

    [Fact]
    public async Task Restore_over_a_database_with_documents_keeps_a_copy_of_it_first()
    {
        await using var provider = await StartAsync();
        await FillAsync(provider);
        await provider.GetRequiredService<IMaintenanceService>().BackupNowAsync(Ct);
        await ImportAsync(provider, "extra", Invoices[0] with { Number = "EXTRA-1" });

        var result = await provider.GetRequiredService<IArchiveService>().RestoreFromBackupAsync(_archive.Root, Ct);

        Assert.Equal(7, result.Value.DocumentCount);
        var aside = Assert.Single(Directory.GetFiles(_appData.Path(SqliteBackup.LocalFolderName)));
        Assert.Equal(9L, CountDocumentsIn(aside));
    }

    // Plan step 5b's "done when": with no backup at all, the archive alone gives the rows back,
    // except what file names can't hold (the partner's exact name, the number's punctuation).
    [Fact]
    public async Task Rebuild_gives_back_the_rows_from_the_folders_and_file_names()
    {
        string[] before;
        await using (var oldPc = await StartAsync())
        {
            await FillAsync(oldPc);
            before = KeySnapshot();
        }
        // A second copy of a file, under a name the app could have written.
        const string original = "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf";
        var duplicate = _archive.Path("2026", "1T", "Recibidas", "Telefonica-Espana", "2026-03-14_Telefonica-Espana_A-2026-0123_3.pdf");
        File.Copy(_archive.Path(original), duplicate);
        DeleteDatabase();

        await using var newPc = await StartAsync(setArchiveRoot: false);
        List<ArchiveProgress> reports = [];
        var result = await newPc.GetRequiredService<IArchiveService>()
            .RebuildIndexAsync(_archive.Root, new SyncProgress<ArchiveProgress>(reports.Add), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(before, KeySnapshot());
        Assert.Equal(["0"], Snapshot("SELECT COUNT(*) FROM Documents WHERE IsPaid = 0")); // names can't say; old ones are settled
        Assert.Equal((8, 7), (result.Value.DocumentCount, result.Value.PartnerCount));
        Assert.Equal([_archive.Path("2026", "escaneo suelto.pdf")], result.Value.Unrecognized);
        Assert.Equal([duplicate], result.Value.Duplicates);
        Assert.Equal((9, 9), (reports[^1].FilesDone, reports[^1].FilesTotal));
        Assert.Equal(_archive.Root, await ArchiveRootSettingAsync(newPc));
    }

    [Fact]
    public async Task Rebuild_into_a_database_with_documents_throws()
    {
        await using var provider = await StartAsync();
        await FillAsync(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<IArchiveService>().RebuildIndexAsync(_archive.Root, null, Ct));
    }

    [Fact]
    public async Task Rebuild_of_a_missing_folder_is_ArchiveUnavailable()
    {
        await using var provider = await StartAsync(setArchiveRoot: false);

        var result = await provider.GetRequiredService<IArchiveService>()
            .RebuildIndexAsync(_elsewhere.Path("nada"), null, Ct);

        Assert.Equal(ErrorCode.ArchiveUnavailable, result.Error);
    }

    // One "start" of the app: build the container, run the startup step.
    private async Task<ServiceProvider> StartAsync(bool setArchiveRoot = true)
    {
        var services = new ServiceCollection().AddBackend(_appData.Root);
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<IFileStorage>(_storage);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await provider.GetRequiredService<IMaintenanceService>().RunStartupAsync(Ct);
        if (setArchiveRoot)
            await provider.GetRequiredService<ISettingsService>().SetArchiveRootAsync(_archive.Root, Ct);
        return provider;
    }

    // Imports every invoice, deletes the Gestoría one, and drops a PDF the app doesn't know into the archive.
    private async Task<int[]> FillAsync(IServiceProvider provider)
    {
        List<int> ids = [];
        foreach (var (i, invoice) in Invoices.Index())
            ids.Add(await ImportAsync(provider, $"invoice {i}", invoice));

        await provider.GetRequiredService<IDocumentService>().DeleteAsync(ids[5], Ct);
        _archive.WriteFile(UnknownPdf, TestPdf.Bytes("dropped in by hand"));
        return [.. ids];
    }

    private async Task<int> ImportAsync(
        IServiceProvider provider,
        string marker,
        (string Partner, string Number, string Date, DocType Type, Direction Direction) invoice)
    {
        var result = await provider.GetRequiredService<IImportService>().ImportAsync(new ImportRequest
        {
            SourcePath = _downloads.WriteFile($"{marker}.pdf", TestPdf.Bytes(marker)),
            PartnerName = invoice.Partner,
            DocNumber = invoice.Number,
            IssueDate = DateOnly.Parse(invoice.Date, CultureInfo.InvariantCulture),
            Type = invoice.Type,
            Direction = invoice.Direction,
            ConfirmDuplicate = true,
        }, Ct);
        return result.Value.Document.Id;
    }

    private static async Task<string?> ArchiveRootSettingAsync(IServiceProvider provider) =>
        (await provider.GetRequiredService<ISettingsService>().GetAsync(Ct)).ArchiveRoot;

    // Like a new PC: no database, no WAL files, no pooled connection still holding them.
    private void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_appData.Path("ordena.db" + suffix));
    }

    // Every column of every document and its partner.
    private string[] FullSnapshot() => Snapshot("""
        SELECT d.Id, d.Sha256, d.PartnerId, d.DocNumber, d.DocNumberNorm, d.IssueDate, d.Type, d.Direction,
               d.RelativePath, d.OriginalName, d.FileSizeBytes, d.ImportedAt, d.UpdatedAt, d.DeletedAt, d.IsPaid,
               p.Name, p.NameNorm, p.FolderName, p.CreatedAt
        FROM Documents d JOIN Partners p ON p.Id = d.PartnerId ORDER BY d.Id
        """);

    // What a rebuild can know from the archive alone.
    private string[] KeySnapshot() => Snapshot("""
        SELECT d.RelativePath, d.Sha256, d.IssueDate, d.Type, d.Direction, d.DocNumberNorm,
               p.FolderName, p.NameNorm, d.DeletedAt IS NOT NULL
        FROM Documents d JOIN Partners p ON p.Id = d.PartnerId ORDER BY d.RelativePath
        """);

    private string[] Snapshot(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={_appData.Path("ordena.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        List<string> rows = [];
        while (reader.Read())
            rows.Add(string.Join(" | ", Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.IsDBNull(i) ? "null" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
        return [.. rows];
    }

    private static long CountDocumentsIn(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Documents";
        return (long)command.ExecuteScalar()!;
    }
}
