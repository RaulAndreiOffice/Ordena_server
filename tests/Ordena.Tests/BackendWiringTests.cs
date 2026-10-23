using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend;
using Ordena.Backend.Data;
using Ordena.Backend.Dtos;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests;

// The backend as the app will use it: AddBackend() on a real database file in a temp folder.
public sealed class BackendWiringTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Ordena.Tests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        // Pooled connections keep ordena.db open, which would block deleting the folder.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task Startup_creates_the_database_with_the_benchmarked_pragmas()
    {
        await using var provider = await StartAsync();

        var database = Path.Combine(_folder, "ordena.db");
        Assert.Equal(database, provider.GetRequiredService<IAppPaths>().Database);
        Assert.True(File.Exists(database));

        await using var db = await provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(Ct);
        await db.Database.OpenConnectionAsync(Ct); // opened by EF, so the interceptor runs
        Assert.Equal("wal", Pragma(db, "journal_mode"));
        Assert.Equal(1L, Pragma(db, "synchronous")); // 1 = NORMAL
        Assert.Equal(-20000L, Pragma(db, "cache_size"));
        Assert.Equal(2L, Pragma(db, "temp_store")); // 2 = MEMORY
    }

    [Fact]
    public async Task Archive_folder_and_language_survive_a_restart()
    {
        await using (var first = await StartAsync())
        {
            var settings = first.GetRequiredService<ISettingsService>();
            await settings.SetArchiveRootAsync(Path.Combine(_folder, "Facturas"), Ct);
            await settings.SetLanguageAsync(UserSettings.English, Ct);
        }

        await using var second = await StartAsync();
        var loaded = await second.GetRequiredService<ISettingsService>().GetAsync(Ct);

        Assert.Equal(Path.Combine(_folder, "Facturas"), loaded.ArchiveRoot);
        Assert.Equal(UserSettings.English, loaded.Language);
    }

    [Fact]
    public async Task Every_service_resolves_and_search_runs_on_a_new_database()
    {
        await using var provider = await StartAsync();

        var result = await provider.GetRequiredService<ISearchService>().SearchAsync(new SearchQuery(), Ct);
        var suggestions = await provider.GetRequiredService<IPartnerService>().SuggestAsync("telef", 10, Ct);
        var trash = await provider.GetRequiredService<IDocumentService>().ListTrashAsync(Ct);
        var rebuild = await provider.GetRequiredService<IArchiveService>().RebuildIndexAsync(_folder, null, Ct);

        Assert.Empty(result.Items);
        Assert.Empty(suggestions);
        Assert.Empty(trash);
        Assert.Equal(0, rebuild.Value.DocumentCount);
    }

    // The backend half of step 2's "done when": dropped PDFs end up correctly named in their
    // Año/Trimestre/Recibidas|Emitidas/Partner folders, the originals are gone and the rows exist.
    [Fact]
    public async Task Imported_pdfs_are_filed_by_year_quarter_direction_and_partner()
    {
        using var archive = new TempFolder();
        using var downloads = new TempFolder();
        await using var provider = await StartAsync();
        await provider.GetRequiredService<ISettingsService>().SetArchiveRootAsync(archive.Root, Ct);
        var import = provider.GetRequiredService<IImportService>();

        (string Partner, string Number, string Date, DocType Type, Direction Direction)[] invoices =
        [
            ("Telefónica España, S.A.U.", "A-2026/0123", "2026-03-14", DocType.Factura, Direction.Recibida),
            ("Repsol S.A.", "T-88812", "2026-03-20", DocType.FacturaSimplificada, Direction.Recibida),
            ("Iberdrola Clientes S.A.U.", "21/0042", "2026-04-02", DocType.FacturaRectificativa, Direction.Recibida),
            ("Cliente Ejemplo S.L.", "F-2026-007", "2026-07-03", DocType.Factura, Direction.Emitida),
            ("Gestoría López", "R-15", "2026-12-31", DocType.Recibo, Direction.Recibida),
        ];
        foreach (var (i, invoice) in invoices.Index())
        {
            var result = await import.ImportAsync(new ImportRequest
            {
                SourcePath = downloads.WriteFile($"scan{i}.pdf", TestPdf.Bytes($"invoice {i}")),
                PartnerName = invoice.Partner,
                DocNumber = invoice.Number,
                IssueDate = DateOnly.Parse(invoice.Date, CultureInfo.InvariantCulture),
                Type = invoice.Type,
                Direction = invoice.Direction,
            }, Ct);
            Assert.True(result.IsSuccess, $"{invoice.Partner}: {result.Error}");
        }

        string[] filed =
        [
            "2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812_Ticket.pdf",
            "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf",
            "2026/2T/Recibidas/Iberdrola-Clientes/2026-04-02_Iberdrola-Clientes_21-0042_Rectif.pdf",
            "2026/3T/Emitidas/Cliente-Ejemplo/2026-07-03_Cliente-Ejemplo_F-2026-007.pdf",
            "2026/4T/Recibidas/Gestoria-Lopez/2026-12-31_Gestoria-Lopez_R-15_Recibo.pdf",
        ];
        Assert.Equal(filed, archive.Files());
        Assert.Empty(downloads.Files());

        var search = provider.GetRequiredService<ISearchService>();
        Assert.Equal(5, (await search.SearchAsync(new SearchQuery(), Ct)).TotalCount);

        // Deleted to _Papelera and restored: the file goes there and comes back to the same place.
        var documents = provider.GetRequiredService<IDocumentService>();
        var repsol = Assert.Single((await search.SearchAsync(new SearchQuery { Text = "repsol" }, Ct)).Items);
        Assert.True((await documents.DeleteAsync(repsol.Id, Ct)).IsSuccess);
        Assert.Contains("_Papelera/" + filed[0], archive.Files());
        Assert.True((await documents.RestoreAsync(repsol.Id, Ct)).IsSuccess);
        Assert.Equal(filed, archive.Files());
    }

    // Like the app: build the container (checking every registration), then run the startup step.
    private async Task<ServiceProvider> StartAsync()
    {
        var provider = new ServiceCollection()
            .AddBackend(_folder)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await provider.GetRequiredService<IMaintenanceService>().RunStartupAsync(Ct);
        return provider;
    }

    private static object? Pragma(AppDbContext db, string name)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA {name}";
        return command.ExecuteScalar();
    }
}
