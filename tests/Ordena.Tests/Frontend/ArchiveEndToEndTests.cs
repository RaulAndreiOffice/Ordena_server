using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Frontend;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// Plan.md step 5b's "done when", through the screens: moving the archive from Ajustes, and on a new PC
// restoring it, or rebuilding its index, from first run, all give back the same documents. The real backend
// on temp folders and the real ViewModels; only what opens windows is faked. One service provider per
// "start" of the app.
[Collection(CultureCollection.Name)]
public sealed class ArchiveEndToEndTests : IDisposable
{
    private readonly CultureScope _spanish = new("es-ES");
    private readonly TempFolder _appData = new();
    private readonly TempFolder _disks = new();
    private readonly TempFolder _downloads = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeFilePicker _picker = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Archive => _disks.Path("Facturas");

    public void Dispose()
    {
        // Pooled connections keep ordena.db open, which would block deleting the folder.
        SqliteConnection.ClearAllPools();
        _appData.Dispose();
        _disks.Dispose();
        _downloads.Dispose();
        _spanish.Dispose();
    }

    [Fact]
    public async Task Ajustes_moves_the_archive_and_every_document_is_found_in_the_new_place()
    {
        await using var provider = await StartAsync();
        await ArchiveSomeAsync(provider);
        var before = await DocumentsAsync(provider);
        var usb = _disks.Path("USB", "Facturas");
        _picker.Choice = usb;

        var settings = provider.GetRequiredService<SettingsViewModel>();
        await settings.OnNavigatedToAsync();
        await settings.Archive.MoveCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Errors);
        Assert.Equal(usb, provider.GetRequiredService<IArchiveState>().Root);
        Assert.Equal(usb, (await provider.GetRequiredService<ISettingsService>().GetAsync(Ct)).ArchiveRoot);
        Assert.Equal(before, await DocumentsAsync(provider));
        foreach (var document in before)
        {
            var path = await provider.GetRequiredService<IDocumentService>().GetFullPathAsync(document.Id, Ct);
            Assert.StartsWith(usb + Path.DirectorySeparatorChar, path.Value);
        }
        Assert.Empty(TempFolder.FilesUnder(Archive)); // deleted once the user agreed
        Assert.Equal(["Carpeta antigua borrada."], _dialogs.Infos);
    }

    [Fact]
    public async Task On_a_new_pc_first_run_restores_the_archive_from_its_backup()
    {
        IReadOnlyList<DocumentListItem> before;
        await using (var oldPc = await StartAsync())
        {
            await ArchiveSomeAsync(oldPc);
            before = await DocumentsAsync(oldPc);
            var settings = oldPc.GetRequiredService<SettingsViewModel>();
            await settings.OnNavigatedToAsync();
            await settings.Archive.BackupNowCommand.ExecuteAsync(null);
            Assert.StartsWith("Última copia: ", settings.Archive.LastBackupText);
        }
        DeleteDatabase();

        await using var newPc = await StartAsync(setArchiveRoot: false);
        Assert.True(await FirstRunWithTheArchiveAsync(newPc));

        Assert.Equal(before, await DocumentsAsync(newPc));
        Assert.StartsWith("Recuperada la copia de seguridad del ", Assert.Single(_dialogs.Infos));
        Assert.Equal(Archive, (await newPc.GetRequiredService<ISettingsService>().GetAsync(Ct)).ArchiveRoot);
    }

    // No backup at all: the folders and file names give back each document's date, number, type,
    // direction and file; the partner's name is its folder's ("Telefonica Espana").
    [Fact]
    public async Task On_a_new_pc_without_a_backup_first_run_rebuilds_the_index_from_the_file_names()
    {
        IReadOnlyList<DocumentListItem> before;
        await using (var oldPc = await StartAsync())
        {
            await ArchiveSomeAsync(oldPc);
            before = await DocumentsAsync(oldPc);
        }
        DeleteDatabase();

        await using var newPc = await StartAsync(setArchiveRoot: false);
        Assert.True(await FirstRunWithTheArchiveAsync(newPc));

        static object Key(DocumentListItem d) => (d.IssueDate, d.DocNumber, d.Type, d.Direction, d.RelativePath);
        Assert.Equal(before.Select(Key), (await DocumentsAsync(newPc)).Select(Key));
        Assert.StartsWith("Índice reconstruido: 3 documentos de 3 proveedores y clientes.", Assert.Single(_dialogs.Infos));
        Assert.Equal(["Reconstruyendo el índice…"], _dialogs.ProgressTitles);
    }

    // One "start" of the app, with everything that opens windows faked.
    private async Task<ServiceProvider> StartAsync(bool setArchiveRoot = true)
    {
        var provider = new ServiceCollection()
            .AddBackend(_appData.Path("data"))
            .AddFrontend()
            .AddSingleton<IMessenger>(new WeakReferenceMessenger())
            .AddSingleton<IPreviewService, FakePreview>()
            .AddSingleton<IDialogService>(_dialogs)
            .AddSingleton<IShellService, FakeShell>()
            .AddSingleton<IFilePickerService>(_picker)
            .BuildServiceProvider();
        await provider.GetRequiredService<IMaintenanceService>().RunStartupAsync(Ct);
        if (setArchiveRoot)
            Assert.True((await provider.GetRequiredService<ISettingsService>().SetArchiveRootAsync(Archive, Ct)).IsSuccess);
        await provider.GetRequiredService<IArchiveState>().RefreshAsync();
        return provider;
    }

    // First run, "Ya tengo un archivo de facturas", on the archive folder. True when it completed.
    private async Task<bool> FirstRunWithTheArchiveAsync(IServiceProvider provider)
    {
        var firstRun = provider.GetRequiredService<FirstRunViewModel>();
        var completed = false;
        firstRun.Completed += (_, _) => completed = true;
        await firstRun.LoadAsync();
        firstRun.HasArchive = true;
        firstRun.ArchiveRoot = Archive;

        await firstRun.StartCommand.ExecuteAsync(null);
        return completed;
    }

    private async Task ArchiveSomeAsync(IServiceProvider provider)
    {
        await ImportAsync(provider, "Telefónica España, S.A.U.", "A-2026-0123", new(2026, 3, 14), DocType.Factura, Direction.Recibida);
        await ImportAsync(provider, "Repsol S.A.", "T-88812", new(2026, 3, 20), DocType.FacturaSimplificada, Direction.Recibida);
        await ImportAsync(provider, "Cliente Ejemplo S.L.", "F-7", new(2025, 11, 5), DocType.Proforma, Direction.Emitida);
    }

    private async Task ImportAsync(
        IServiceProvider provider, string partner, string number, DateOnly date, DocType type, Direction direction)
    {
        var result = await provider.GetRequiredService<IImportService>().ImportAsync(new ImportRequest
        {
            SourcePath = _downloads.WriteFile($"{number}.pdf", TestPdf.Bytes(number)),
            PartnerName = partner,
            DocNumber = number,
            IssueDate = date,
            Type = type,
            Direction = direction,
        }, Ct);
        Assert.True(result.IsSuccess, $"{number}: {result.Error}");
    }

    // What Buscar shows, in a fixed order.
    private static async Task<IReadOnlyList<DocumentListItem>> DocumentsAsync(IServiceProvider provider) =>
        (await provider.GetRequiredService<ISearchService>().SearchAsync(new SearchQuery(), Ct)).Items
            .OrderBy(d => d.RelativePath, StringComparer.Ordinal)
            .ToList();

    // Like a new PC: no database, no WAL files, no pooled connection still holding them.
    private void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_appData.Path("data", "ordena.db" + suffix));
    }
}
