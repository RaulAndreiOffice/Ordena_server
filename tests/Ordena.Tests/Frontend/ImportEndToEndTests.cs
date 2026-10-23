using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Frontend;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// Plan.md step 2's "done when": five PDFs queued on the import screen and filled in end up named and
// filed in their Año/Trimestre/Recibidas|Emitidas/Partner folders, the originals are gone and the
// documents are in the database. The real backend on a temp folder; only what opens windows is faked.
[Collection(CultureCollection.Name)]
public sealed class ImportEndToEndTests : IDisposable
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
    public async Task Five_pdfs_filled_in_are_filed_under_their_standard_names_and_found()
    {
        var messenger = new WeakReferenceMessenger();
        await using var provider = new ServiceCollection()
            .AddBackend(_appData.Path("data"))
            .AddFrontend()
            // The last registration wins: a messenger of this test's own, and no windows.
            .AddSingleton<IMessenger>(messenger)
            .AddSingleton<IPreviewService, FakePreview>()
            .AddSingleton<IDialogService, FakeDialogs>()
            .AddSingleton<IShellService, FakeShell>()
            .AddSingleton<IFilePickerService, FakeFilePicker>()
            .BuildServiceProvider();
        await provider.GetRequiredService<IMaintenanceService>().RunStartupAsync(Ct);
        var archive = _appData.Path("Facturas");
        Assert.True((await provider.GetRequiredService<ISettingsService>().SetArchiveRootAsync(archive, Ct)).IsSuccess);

        Filled[] files =
        [
            new("scan 001.pdf", "Telefónica España, S.A.U.", "A-2026/0123", new(2026, 3, 14), DocType.Factura, Direction.Recibida,
                "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf"),
            // The same partner written another way: filed in the same folder, under the same name.
            new("scan 002.pdf", "telefonica España", "R-7", new(2026, 3, 20), DocType.FacturaRectificativa, Direction.Recibida,
                "2026/1T/Recibidas/Telefonica-Espana/2026-03-20_Telefonica-Espana_R-7_Rectif.pdf"),
            new("ticket gasolina.pdf", "Repsol S.A.", "T-88812", new(2026, 4, 2), DocType.FacturaSimplificada, Direction.Recibida,
                "2026/2T/Recibidas/Repsol/2026-04-02_Repsol_T-88812_Ticket.pdf"),
            new("luz diciembre.pdf", "Iberdrola", "F-77", new(2025, 12, 31), DocType.Recibo, Direction.Recibida,
                "2025/4T/Recibidas/Iberdrola/2025-12-31_Iberdrola_F-77_Recibo.pdf"),
            new("factura cliente.pdf", "Ñandú & Cía", "2026/045", new(2026, 7, 1), DocType.Factura, Direction.Emitida,
                "2026/3T/Emitidas/Nandu-Cia/2026-07-01_Nandu-Cia_2026-045.pdf"),
        ];
        var sources = files.Select(f => _downloads.WriteFile(f.FileName, TestPdf.Bytes(f.FileName))).ToArray();
        var changes = new List<DocumentsChangedMessage>();
        messenger.Register<DocumentsChangedMessage>(this, (_, message) => changes.Add(message));

        // What a drop does once the Import page is up.
        var import = provider.GetRequiredService<ImportViewModel>();
        import.Form.SuggestionDelay = TimeSpan.Zero;
        await import.EnqueueAsync(sources);

        foreach (var file in files)
        {
            Assert.Equal(file.FileName, import.CurrentItem?.FileName);
            Assert.True(import.CanFill, $"{file.FileName}: {import.ProblemText}");

            import.Form.IsReceived = file.Direction == Direction.Recibida;
            import.Form.PartnerName = file.Partner;
            import.Form.DocNumber = file.Number;
            import.Form.IssueDate = file.Date.ToDateTime(TimeOnly.MinValue);
            import.Form.SelectedType = import.Form.Types.Single(t => t.Type == file.Type);
            await import.SaveCommand.ExecuteAsync(null);
        }

        Assert.True(import.IsFinished);
        Assert.Equal("Archivados: 5 · Omitidos: 0", import.Summary);
        Assert.Equal(files.Select(f => $"Archivado como {f.Expected}"), import.Queue.Select(i => i.Message));
        Assert.Equal(5, changes.Count);

        // Each file under its standard name, unchanged; nothing left in Descargas.
        Assert.Equal(files.Select(f => f.Expected).Order(StringComparer.Ordinal), TempFolder.FilesUnder(archive));
        foreach (var file in files)
            Assert.Equal(TestPdf.Bytes(file.FileName), File.ReadAllBytes(Path.Combine(archive, file.Expected)));
        Assert.Empty(_downloads.Files());

        var found = await provider.GetRequiredService<ISearchService>().SearchAsync(new SearchQuery(), Ct);
        Assert.Equal(5, found.TotalCount);
        Assert.Equal(files.Select(f => f.Expected).Order(), found.Items.Select(i => i.RelativePath).Order());
        Assert.Equal(2, found.Items.Count(i => i.PartnerName == "Telefónica España, S.A.U."));
    }

    private sealed record Filled(
        string FileName, string Partner, string Number, DateOnly Date, DocType Type, Direction Direction, string Expected);
}
