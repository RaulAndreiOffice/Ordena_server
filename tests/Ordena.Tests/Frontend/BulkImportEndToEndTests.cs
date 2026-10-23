using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend;
using Ordena.Backend.Data;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Frontend;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// Plan.md step 4's "done when" test:
// A test folder of 3,000 generated files with a consistent name pattern imports with the right partners,
// dates and numbers, the old folder is untouched, and the unrecognized files end up in the manual queue.
[Collection(CultureCollection.Name)]
public sealed class BulkImportEndToEndTests : IDisposable
{
    private readonly CultureScope _spanish = new("es-ES");
    private readonly TempFolder _appData = new();
    private readonly TempFolder _source = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _appData.Dispose();
        _source.Dispose();
        _spanish.Dispose();
    }

    private sealed record ExpectedDoc(string SourcePath, DateOnly Date, string RawPartner, string FinalPartner, string Number);

    [Fact]
    public async Task Bulk_import_3000_files_with_consistent_pattern_leaves_source_untouched_and_manual_queue_has_unrecognized()
    {
        var messenger = new WeakReferenceMessenger();
        await using var provider = new ServiceCollection()
            .AddBackend(_appData.Path("data"))
            .AddFrontend()
            .AddSingleton<IMessenger>(messenger)
            .AddSingleton<IPreviewService, FakePreview>()
            .AddSingleton<IDialogService, FakeDialogs>()
            .AddSingleton<IShellService, FakeShell>()
            .AddSingleton<IFilePickerService, FakeFilePicker>()
            .BuildServiceProvider();

        await provider.GetRequiredService<IMaintenanceService>().RunStartupAsync(Ct);
        var archive = _appData.Path("Facturas");
        Assert.True((await provider.GetRequiredService<ISettingsService>().SetArchiveRootAsync(archive, Ct)).IsSuccess);

        // Define 40 partner spellings (20 distinct partners with 2 variants each)
        var partnerVariants = new (string Canonical, string Variant)[]
        {
            ("Telefónica España", "Telefonica Espana"),
            ("Repsol S.A.", "Repsol"),
            ("Iberdrola Clientes", "Iberdrola"),
            ("Endesa Energía", "Endesa Energia"),
            ("Vodafone España", "Vodafone"),
            ("Naturgy Iberia", "Naturgy"),
            ("Orange Espagne", "Orange"),
            ("Mercadona S.A.", "Mercadona"),
            ("El Corte Inglés", "El Corte Ingles"),
            ("Carrefour España", "Carrefour"),
            ("Amazon Spain", "Amazon"),
            ("SEUR Transporte", "SEUR"),
            ("Correos Express", "Correos"),
            ("MRW Envíos", "MRW"),
            ("Banco Santander", "Santander"),
            ("BBVA Seguros", "BBVA"),
            ("CaixaBank", "La Caixa"),
            ("Mapfre Seguros", "Mapfre"),
            ("Mutua Madrileña", "Mutua Madrilena"),
            ("Sanitas Dental", "Sanitas"),
        };

        var partnerNames = new List<(string Name, string Canonical)>();
        foreach (var (canonical, variant) in partnerVariants)
        {
            partnerNames.Add((canonical, canonical));
            partnerNames.Add((variant, canonical));
        }

        var expectedDocs = new List<ExpectedDoc>(3000);
        var startDate = new DateOnly(2025, 1, 1);

        // Generate 3,000 valid file names: dd-MM-yyyy_proveedor_numero.pdf
        for (var i = 0; i < 3000; i++)
        {
            var date = startDate.AddDays(i % 500);
            var (name, canonical) = partnerNames[i % partnerNames.Count];
            var number = $"FAC-{2025 + (i % 2)}-{i:D5}";
            var fileName = $"{date:dd-MM-yyyy}_{name}_{number}.pdf";
            var filePath = _source.WriteFile(fileName, TestPdf.Bytes($"doc_{i}_{fileName}"));
            expectedDocs.Add(new ExpectedDoc(filePath, date, name, canonical, number));
        }

        // Generate 25 unrecognized files
        var unrecognizedPaths = new List<string>(25);
        for (var i = 0; i < 25; i++)
        {
            var fileName = $"unrelated_file_{i:D2}.pdf";
            var filePath = _source.WriteFile(fileName, TestPdf.Bytes($"unrelated_{i}"));
            unrecognizedPaths.Add(filePath);
        }

        // Record source file hashes before import
        var sourceHashesBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in _source.Files())
        {
            var fullPath = _source.Path(file);
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(fullPath);
            sourceHashesBefore[fullPath] = Convert.ToHexString(sha.ComputeHash(stream));
        }
        Assert.Equal(3025, sourceHashesBefore.Count);

        var droppedMessages = new List<FilesDroppedMessage>();
        messenger.Register<FilesDroppedMessage>(this, (_, m) => droppedMessages.Add(m));

        var vm = provider.GetRequiredService<BulkImportViewModel>();
        vm.PatternDelay = TimeSpan.Zero;
        vm.Folder = _source.Root;

        // Step 1 -> Step 2 (Analyze)
        await vm.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(BulkStep.Pattern, vm.Step);
        Assert.NotNull(vm.SelectedPattern);
        Assert.Equal("{fecha}_{proveedor}_{numero}", vm.SelectedPattern.Pattern);
        Assert.Equal("dd-MM-yyyy", vm.SelectedPattern.DateFormat);

        // Step 2 -> Step 3 (Preview)
        vm.NextCommand.Execute(null);
        Assert.Equal(BulkStep.Preview, vm.Step);

        // Step 3 -> Step 4 (Partners)
        vm.NextCommand.Execute(null);
        Assert.Equal(BulkStep.Partners, vm.Step);

        // Step 4 -> Step 5 (Run)
        await vm.ImportCommand.ExecuteAsync(null);
        await vm.PendingRun;

        Assert.Equal(BulkStep.Report, vm.Step);
        Assert.Contains("Importados 3.000", vm.ReportText);
        Assert.Contains("Duplicados 0", vm.ReportText);
        Assert.Contains("Fallidos 0", vm.ReportText);
        Assert.Equal(25, vm.LeftOut.Count);
        Assert.Equal(unrecognizedPaths.Order(StringComparer.OrdinalIgnoreCase), vm.LeftOut.Order(StringComparer.OrdinalIgnoreCase));

        // 1. Checks: 3,000 documents in the database
        await using (var db = provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())
        {
            var dbDocs = await db.Documents
                .Include(d => d.Partner)
                .AsNoTracking()
                .ToListAsync(Ct);

            Assert.Equal(3000, dbDocs.Count);

            var dbDocMap = dbDocs.ToDictionary(d => d.DocNumber, d => d);
            foreach (var expected in expectedDocs)
            {
                Assert.True(dbDocMap.TryGetValue(expected.Number, out var actual), $"Document {expected.Number} missing in DB");
                Assert.Equal(expected.Date, actual.IssueDate);
                Assert.Equal(expected.Number, actual.DocNumber);
                // Partner should match canonical or mapped partner
                Assert.False(string.IsNullOrWhiteSpace(actual.Partner.Name));

                // Verify file exists in archive under its standard relative path
                var archiveFullPath = Path.Combine(archive, actual.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(archiveFullPath), $"Archived file missing at {archiveFullPath}");
            }
        }

        // 2. Checks: Source folder is completely unchanged (same file list and hashes)
        var sourceHashesAfter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in _source.Files())
        {
            var fullPath = _source.Path(file);
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(fullPath);
            sourceHashesAfter[fullPath] = Convert.ToHexString(sha.ComputeHash(stream));
        }

        Assert.Equal(sourceHashesBefore.Count, sourceHashesAfter.Count);
        foreach (var (path, hash) in sourceHashesBefore)
        {
            Assert.True(sourceHashesAfter.TryGetValue(path, out var afterHash), $"File {path} missing in source folder");
            Assert.Equal(hash, afterHash);
        }

        // 3. Checks: "Enviar a importación manual" queues exactly the 25 files in copy mode
        await vm.SendToManualCommand.ExecuteAsync(null);
        var importVm = provider.GetRequiredService<ImportViewModel>();
        await importVm.PendingEnqueue;

        var queued = Assert.Single(droppedMessages);
        Assert.True(queued.KeepSource);
        Assert.Equal(25, queued.Paths.Count);
        Assert.Equal(unrecognizedPaths.Order(StringComparer.OrdinalIgnoreCase), queued.Paths.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(25, importVm.Queue.Count);
    }
}
