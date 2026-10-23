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

// Plan.md step 3's "done when", second half: edit, delete and restore from the search and trash pages
// keep the disk and the database consistent. The real backend on temp folders and the real ViewModels;
// only what opens windows is faked. After every step the files on disk are exactly the documents' paths.
[Collection(CultureCollection.Name)]
public sealed class ManageEndToEndTests : IDisposable
{
    private readonly CultureScope _spanish = new("es-ES");
    private readonly TempFolder _appData = new();
    private readonly TempFolder _downloads = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        // Pooled connections keep ordena.db open, which would block deleting the folder.
        SqliteConnection.ClearAllPools();
        _appData.Dispose();
        _downloads.Dispose();
        _spanish.Dispose();
    }

    [Fact]
    public async Task Edit_delete_and_restore_keep_the_files_and_the_rows_in_step()
    {
        await using var provider = new ServiceCollection()
            .AddBackend(_appData.Path("data"))
            .AddFrontend()
            // The last registration wins: a messenger of this test's own, and no windows.
            .AddSingleton<IMessenger>(new WeakReferenceMessenger())
            .AddSingleton<IPreviewService, FakePreview>()
            .AddSingleton<IDialogService, FakeDialogs>()
            .AddSingleton<IShellService, FakeShell>()
            .AddSingleton<IFilePickerService, FakeFilePicker>()
            .BuildServiceProvider();
        await provider.GetRequiredService<IMaintenanceService>().RunStartupAsync(Ct);
        var archive = _appData.Path("Facturas");
        Assert.True((await provider.GetRequiredService<ISettingsService>().SetArchiveRootAsync(archive, Ct)).IsSuccess);

        await ImportAsync(provider, "telefonica.pdf", "Telefónica España, S.A.U.", "A-1", new(2026, 3, 14), DocType.Factura);
        await ImportAsync(provider, "repsol.pdf", "Repsol S.A.", "T-2", new(2026, 4, 2), DocType.FacturaSimplificada);
        await ImportAsync(provider, "iberdrola.pdf", "Iberdrola", "F-3", new(2025, 12, 31), DocType.Recibo);

        var search = provider.GetRequiredService<SearchViewModel>();
        search.SearchDelay = TimeSpan.Zero;
        await search.OnNavigatedToAsync();
        Assert.Equal(new[] { "A-1", "F-3", "T-2" }, search.Results.Select(r => r.DocNumber).Order());
        Assert.Equal(["2026", "2025"], search.Years.Skip(1).Select(y => y.Label));
        await AssertConsistentAsync(provider, archive);

        // The first quarter only.
        search.SelectedQuarter = search.Quarters[1];
        await search.PendingSearch;
        var telefonica = Assert.Single(search.Results);
        await search.ClearFiltersCommand.ExecuteAsync(null);

        // Edit: a date in the next quarter moves the file to 2T, and the grid shows the new path.
        var edit = provider.GetRequiredService<EditDocumentViewModel>();
        await edit.LoadAsync(telefonica.Id);
        edit.Form.IssueDate = new DateTime(2026, 4, 20);
        await edit.SaveCommand.ExecuteAsync(null);
        await search.PendingSearch;

        const string Moved = "2026/2T/Recibidas/Telefonica-Espana/2026-04-20_Telefonica-Espana_A-1.pdf";
        Assert.Equal(Moved, search.Results.Single(r => r.Id == telefonica.Id).RelativePath);
        Assert.True(File.Exists(Path.Combine(archive, Moved)));
        await AssertConsistentAsync(provider, archive);

        // Delete: to _Papelera, out of the grid, into the trash page.
        await search.DeleteCommand.ExecuteAsync(search.Results.Single(r => r.Id == telefonica.Id));
        await search.PendingSearch;

        Assert.DoesNotContain(search.Results, r => r.Id == telefonica.Id);
        Assert.Equal("2 documentos", search.StatusText);
        Assert.True(File.Exists(Path.Combine(archive, "_Papelera", Moved)));
        var trash = provider.GetRequiredService<TrashViewModel>();
        await trash.OnNavigatedToAsync();
        Assert.Equal([telefonica.Id], trash.Items.Select(i => i.Id));
        await AssertConsistentAsync(provider, archive);

        // Restore: back to its folder and into the grid.
        await trash.RestoreCommand.ExecuteAsync(trash.Items[0]);
        await search.PendingSearch;

        Assert.Empty(trash.Items);
        Assert.Equal(Moved, search.Results.Single(r => r.Id == telefonica.Id).RelativePath);
        Assert.True(File.Exists(Path.Combine(archive, Moved)));
        await AssertConsistentAsync(provider, archive);
    }

    private async Task ImportAsync(
        IServiceProvider provider, string fileName, string partner, string number, DateOnly date, DocType type)
    {
        var source = _downloads.WriteFile(fileName, TestPdf.Bytes(fileName));
        var result = await provider.GetRequiredService<IImportService>().ImportAsync(new ImportRequest
        {
            SourcePath = source,
            PartnerName = partner,
            DocNumber = number,
            IssueDate = date,
            Type = type,
            Direction = Direction.Recibida,
        }, Ct);
        Assert.True(result.IsSuccess, $"{fileName}: {result.Error}");
    }

    // Every file in the archive (backups aside) belongs to a document, and every document's file is there,
    // deleted ones included.
    private static async Task AssertConsistentAsync(IServiceProvider provider, string archive)
    {
        var active = await provider.GetRequiredService<ISearchService>().SearchAsync(new SearchQuery(), Ct);
        var deleted = await provider.GetRequiredService<IDocumentService>().ListTrashAsync(Ct);
        var expected = active.Items.Select(d => d.RelativePath).Concat(deleted.Select(d => d.RelativePath));

        var onDisk = TempFolder.FilesUnder(archive).Where(f => !f.StartsWith("_backup/", StringComparison.Ordinal));
        Assert.Equal(expected.Order(StringComparer.Ordinal), onDisk);
    }
}
