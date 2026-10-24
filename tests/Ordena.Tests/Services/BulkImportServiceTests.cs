using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

// "Importar carpeta" against a real SQLite database and real folders: an old archive in _old, copied
// into _archive. The old folder must come out exactly as it went in.
public sealed class BulkImportServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 3, 15, 9, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _db = new();
    private readonly TempFolder _archive = new();
    private readonly TempFolder _old = new();
    private readonly ImportService _import;
    private readonly BulkImportService _bulk;

    public BulkImportServiceTests()
    {
        var clock = new FixedClock(Now);
        var storage = new FileStorage(TimeSpan.FromMilliseconds(1));
        var resolver = new PartnerResolver(clock);
        _import = new ImportService(
            _db.UnitOfWorkFactory, storage, resolver, new ArchiveMover(storage, clock, NullLogger<ArchiveMover>.Instance), clock);
        _bulk = new BulkImportService(_db.UnitOfWorkFactory, storage, resolver, _import);
        _db.Save(new AppSetting { Key = "ArchiveRoot", Value = _archive.Root });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _archive.Dispose();
        _old.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task Analyze_reads_every_pdf_name_and_marks_what_needs_a_look()
    {
        var telefonica = _db.Save(TestData.NewPartner());
        _db.Save(TestData.NewDocument(telefonica.Id, docNumber: "A-1"));
        Write("14-03-2026_Repsol_T-1.pdf");
        Write("15-03-2026_Repsol_T-2.pdf");
        Write("16-03-2026_Repsol_T 2.pdf"); // the same number written another way
        Write("30-02-2026_Repsol_T-3.pdf");
        Write("17-03-2026_Telefonica Espana_A-1.pdf"); // already in the archive
        Write("escaneo.pdf");
        Write("sub/18-03-2026_Iberdrola_F-9.pdf");
        Write("notas.txt");

        var preview = (await _bulk.AnalyzeAsync(Options(), Ct)).Value;

        Assert.Equal(new NamePattern("{fecha}_{proveedor}_{numero}", "dd-MM-yyyy", 5, 7), preview.Pattern);
        Assert.Equal(preview.Pattern, preview.Detected[0]);
        Assert.Equal(
        [
            ("14-03-2026_Repsol_T-1.pdf", BulkRowStatus.Ok, "2026-03-14", "Repsol", "T-1"),
            ("15-03-2026_Repsol_T-2.pdf", BulkRowStatus.Ok, "2026-03-15", "Repsol", "T-2"),
            ("16-03-2026_Repsol_T 2.pdf", BulkRowStatus.NumberExists, "2026-03-16", "Repsol", "T 2"),
            ("17-03-2026_Telefonica Espana_A-1.pdf", BulkRowStatus.NumberExists, "2026-03-17", "Telefonica Espana", "A-1"),
            ("30-02-2026_Repsol_T-3.pdf", BulkRowStatus.InvalidDate, null, "Repsol", "T-3"),
            ("escaneo.pdf", BulkRowStatus.NotRecognized, null, null, null),
            (Path.Combine("sub", "18-03-2026_Iberdrola_F-9.pdf"), BulkRowStatus.Ok, "2026-03-18", "Iberdrola", "F-9"),
        ], preview.Rows.Select(r => (r.RelativePath, r.Status, r.IssueDate?.ToString("yyyy-MM-dd"), r.PartnerName, r.DocNumber)));
        Assert.All(preview.Rows, r => Assert.Equal(Path.Combine(_old.Root, r.RelativePath), r.Path));
    }

    [Fact]
    public async Task Partners_are_counted_and_matched_with_the_archives_own()
    {
        _db.Save(TestData.NewPartner(name: "Telefónica España, S.A.U."));
        Write("14-03-2026_Repsol_1.pdf");
        Write("15-03-2026_Repsol_2.pdf");
        Write("16-03-2026_Telefonica Espana_3.pdf");

        var preview = (await _bulk.AnalyzeAsync(Options(), Ct)).Value;

        Assert.Equal(
            [new BulkPartner("Repsol", 2, null), new BulkPartner("Telefonica Espana", 1, "Telefónica España, S.A.U.")],
            preview.Partners);
    }

    // "Repsol" and "Repsol Butano" may be one supplier; a short "Telefonica" is offered the archive's full name.
    [Fact]
    public async Task Similar_names_are_grouped_with_a_suggested_name()
    {
        _db.Save(TestData.NewPartner(name: "Telefónica España, S.A.U."));
        Write("14-03-2026_Repsol_1.pdf");
        Write("15-03-2026_Repsol_2.pdf");
        Write("16-03-2026_Repsol Butano_3.pdf");
        Write("17-03-2026_REPSOL_4.pdf");
        Write("18-03-2026_Telefonica_5.pdf");
        Write("19-03-2026_Iberdrola_6.pdf");

        var groups = (await _bulk.AnalyzeAsync(Options(), Ct)).Value.Groups;

        Assert.Equal(2, groups.Count);
        var repsol = Assert.Single(groups, g => g.SuggestedName == "Repsol");
        Assert.Equal(["REPSOL", "Repsol", "Repsol Butano"], repsol.Names.Order(StringComparer.Ordinal));
        var telefonica = Assert.Single(groups, g => g.SuggestedName == "Telefónica España, S.A.U.");
        Assert.Equal(["Telefonica"], telefonica.Names);
    }

    [Fact]
    public async Task Equally_used_spellings_suggest_the_more_complete_one()
    {
        Write("14-03-2026_Iberdrola_1.pdf");
        Write("15-03-2026_Iberdrola Clientes_2.pdf");

        var group = Assert.Single((await _bulk.AnalyzeAsync(Options(), Ct)).Value.Groups);

        Assert.Equal("Iberdrola Clientes", group.SuggestedName);
    }

    [Fact]
    public async Task A_typed_pattern_is_used_instead_of_the_detected_one()
    {
        Write("Repsol_T-1_14-03-2026.pdf");

        var preview = (await _bulk.AnalyzeAsync(Options() with { Pattern = "{proveedor}_{numero}_{fecha}", DateFormat = "dd-MM-yyyy" }, Ct)).Value;

        Assert.Equal("{proveedor}_{numero}_{fecha}", preview.Pattern?.Pattern);
        Assert.Equal(("Repsol", "T-1", BulkRowStatus.Ok), (preview.Rows[0].PartnerName, preview.Rows[0].DocNumber, preview.Rows[0].Status));
    }

    [Fact]
    public async Task A_typed_pattern_that_cannot_be_used_says_why()
    {
        var result = await _bulk.AnalyzeAsync(Options() with { Pattern = "{fecha}_{importe}", DateFormat = "dd-MM-yyyy" }, Ct);

        Assert.Equal((ErrorCode.InvalidNamePattern, "UnknownToken"), (result.Error, result.Detail));
    }

    [Fact]
    public async Task Names_without_a_date_take_the_files_date_to_be_checked()
    {
        var file = Write("Repsol - T-1.pdf");
        File.SetLastWriteTime(file, new DateTime(2025, 11, 20, 10, 0, 0));

        var row = Assert.Single((await _bulk.AnalyzeAsync(Options(), Ct)).Value.Rows);

        Assert.Equal((BulkRowStatus.CheckDate, new DateOnly(2025, 11, 20), "Repsol", "T-1"), (row.Status, row.IssueDate, row.PartnerName, row.DocNumber));
    }

    [Fact]
    public async Task With_one_folder_per_supplier_the_folder_names_the_partner()
    {
        Write("Repsol_Butano/14-03-2026_T-1.pdf");

        var row = Assert.Single((await _bulk.AnalyzeAsync(Options() with { PartnerFromFolder = true }, Ct)).Value.Rows);

        Assert.Equal((BulkRowStatus.Ok, "Repsol Butano", "T-1"), (row.Status, row.PartnerName, row.DocNumber));
    }

    [Fact]
    public async Task The_archive_itself_and_missing_folders_are_refused()
    {
        var inside = await _bulk.AnalyzeAsync(Options() with { Folder = _archive.Path("2026") }, Ct);
        Directory.CreateDirectory(_archive.Path("2026"));
        var nested = await _bulk.AnalyzeAsync(Options() with { Folder = _archive.Path("2026") }, Ct);
        var containing = await _bulk.AnalyzeAsync(Options() with { Folder = Path.GetDirectoryName(_archive.Root)! }, Ct);

        Assert.Equal(ErrorCode.FileMissing, inside.Error);
        Assert.Equal(ErrorCode.InvalidArchiveFolder, nested.Error);
        Assert.Equal(ErrorCode.InvalidArchiveFolder, containing.Error);
    }

    [Fact]
    public async Task Without_the_archive_folder_nothing_is_analyzed()
    {
        Directory.Delete(_archive.Root);

        Assert.Equal(ErrorCode.ArchiveUnavailable, (await _bulk.AnalyzeAsync(Options(), Ct)).Error);
    }

    // The old folder is the user's backup: every file must still be there, byte for byte.
    [Fact]
    public async Task Run_copies_into_the_archive_and_leaves_the_old_folder_as_it_was()
    {
        Write("14-03-2026_Repsol S.A._T-1.pdf");
        Write("sub/02-04-2026_Iberdrola_F-9.pdf");
        var before = Snapshot(_old.Root);
        var preview = (await _bulk.AnalyzeAsync(Options(), Ct)).Value;

        var report = await _bulk.RunAsync(Run(preview.Rows), null, Ct);

        Assert.Equal(2, report.Imported);
        Assert.Empty(report.Failed);
        Assert.False(report.Cancelled);
        Assert.Equal(before, Snapshot(_old.Root));
        Assert.Equal(
            ["2026/1T/Recibidas/Repsol/2026-03-14_Repsol_T-1.pdf", "2026/2T/Recibidas/Iberdrola/2026-04-02_Iberdrola_F-9.pdf"],
            _archive.Files());
        Assert.Equal(TestPdf.Bytes("14-03-2026_Repsol S.A._T-1.pdf"), File.ReadAllBytes(_archive.Path("2026/1T/Recibidas/Repsol/2026-03-14_Repsol_T-1.pdf")));

        using var db = _db.CreateContext();
        Assert.Equal(["Iberdrola", "Repsol S.A."], await db.Partners.Select(p => p.Name).OrderBy(n => n).ToListAsync(Ct));
    }

    // «Marcar todas como pagadas o cobradas», on by default: an old archive is normally settled.
    [Theory]
    [InlineData(true, 0L)]
    [InlineData(false, 2L)]
    public async Task The_run_marks_every_document_paid_or_pending(bool markPaid, long pending)
    {
        Write("14-03-2026_Repsol S.A._T-1.pdf");
        Write("02-04-2026_Iberdrola_F-9.pdf");
        var preview = (await _bulk.AnalyzeAsync(Options(), Ct)).Value;

        await _bulk.RunAsync(Run(preview.Rows) with { MarkPaid = markPaid }, null, Ct);

        Assert.Equal(2L, _db.Scalar("SELECT COUNT(*) FROM Documents"));
        Assert.Equal(pending, _db.Scalar("SELECT COUNT(*) FROM Documents WHERE IsPaid = 0"));
    }

    [Fact]
    public async Task The_partner_map_merges_names_before_import()
    {
        Write("14-03-2026_Repsol_1.pdf");
        Write("15-03-2026_Repsol Butano_2.pdf");
        var preview = (await _bulk.AnalyzeAsync(Options(), Ct)).Value;

        await _bulk.RunAsync(Run(preview.Rows, new Dictionary<string, string> { ["Repsol Butano"] = "Repsol" }), null, Ct);

        using var db = _db.CreateContext();
        Assert.Equal(["Repsol"], await db.Partners.Select(p => p.Name).ToListAsync(Ct));
        Assert.Equal(2, await db.Documents.CountAsync(Ct));
    }

    // The same bytes under two names in the folder, and a file imported twice: one copy each.
    [Fact]
    public async Task Files_already_archived_are_skipped_as_duplicates()
    {
        var first = Write("14-03-2026_Repsol_1.pdf");
        _old.WriteFile("15-03-2026_Repsol_2.pdf", File.ReadAllBytes(first));
        var rows = (await _bulk.AnalyzeAsync(Options(), Ct)).Value.Rows;

        var report = await _bulk.RunAsync(Run(rows), null, Ct);
        var again = await _bulk.RunAsync(Run([rows[0]]), null, Ct);

        Assert.Equal(1, report.Imported);
        Assert.Equal([rows[1].Path], report.Duplicates);
        Assert.Equal(0, again.Imported);
        Assert.Equal([rows[0].Path], again.Duplicates);
    }

    [Fact]
    public async Task A_number_the_partner_has_is_imported_only_when_included()
    {
        Write("14-03-2026_Repsol_T-1.pdf");
        Write("15-03-2026_Repsol_T-1.pdf");
        var rows = (await _bulk.AnalyzeAsync(Options(), Ct)).Value.Rows;

        var skipped = await _bulk.RunAsync(Run(rows), null, Ct);
        var included = await _bulk.RunAsync(Run([rows[1]], includeDuplicates: true), null, Ct);

        Assert.Equal(BulkRowStatus.NumberExists, rows[1].Status);
        Assert.Equal(1, skipped.Imported);
        Assert.Equal([rows[1].Path], skipped.NumberExists);
        Assert.Equal(1, included.Imported);
    }

    // Stopping between files leaves exactly what was copied, each file with its row.
    [Fact]
    public async Task A_cancelled_run_keeps_what_it_copied_and_nothing_else()
    {
        for (var day = 1; day <= 6; day++)
            Write($"{day:D2}-03-2026_Repsol_T-{day}.pdf");
        var rows = (await _bulk.AnalyzeAsync(Options(), Ct)).Value.Rows;
        using var cancel = new CancellationTokenSource();
        var reports = new List<BulkImportProgress>();
        var progress = new SyncProgress<BulkImportProgress>(p =>
        {
            reports.Add(p);
            if (p.Done == 2)
                cancel.Cancel();
        });

        var report = await _bulk.RunAsync(Run(rows), progress, cancel.Token);

        Assert.True(report.Cancelled);
        Assert.Equal(2, report.Imported);
        Assert.Equal([(1, 6), (2, 6)], reports.Select(r => (r.Done, r.Total)));
        using var db = _db.CreateContext();
        Assert.Equal(_archive.Files(), (await db.Documents.Select(d => d.RelativePath).ToListAsync(Ct)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Items_without_a_partner_or_number_are_a_bug()
    {
        var item = new BulkImportItem(_old.Path("x.pdf"), new DateOnly(2026, 3, 14), " ", "1", DocType.Factura, false);

        await Assert.ThrowsAsync<ArgumentException>(() => _bulk.RunAsync(
            new BulkImportRun(Direction.Recibida, [item], new Dictionary<string, string>()), null, Ct));
    }

    private BulkImportOptions Options() => new() { Folder = _old.Root, Direction = Direction.Recibida };

    // Each file's own name as its content marker, so every file has another hash.
    private string Write(string relativePath) => _old.WriteFile(relativePath, TestPdf.Bytes(Path.GetFileName(relativePath)));

    private static BulkImportRun Run(
        IEnumerable<BulkImportRow> rows, Dictionary<string, string>? map = null, bool includeDuplicates = false) =>
        new(Direction.Recibida,
            rows.Where(r => r.IssueDate is not null && r.PartnerName is not null && r.DocNumber is not null)
                .Select(r => new BulkImportItem(r.Path, r.IssueDate!.Value, r.PartnerName!, r.DocNumber!, DocType.Factura, includeDuplicates))
                .ToList(),
            map ?? []);

    private static string[] Snapshot(string folder) =>
        TempFolder.FilesUnder(folder).Select(f => $"{f}:{Sha256Hasher.Compute(File.ReadAllBytes(Path.Combine(folder, f)))}").ToArray();
}
