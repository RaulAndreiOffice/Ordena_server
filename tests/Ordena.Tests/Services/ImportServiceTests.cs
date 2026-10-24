using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

public sealed class ImportServiceTests : IDisposable
{
    private const string Telefonica = "Telefónica España, S.A.U.";
    private const string TelefonicaPath =
        "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf";

    private static readonly DateTime Now = new(2026, 3, 15, 9, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _db = new();
    private readonly TempFolder _archive = new();
    private readonly TempFolder _downloads = new();
    private readonly ImportService _import;

    public ImportServiceTests()
    {
        var clock = new FixedClock(Now);
        var storage = new FileStorage(TimeSpan.FromMilliseconds(1));
        _import = new ImportService(
            _db.UnitOfWorkFactory, storage, new PartnerResolver(clock),
            new ArchiveMover(storage, clock, NullLogger<ArchiveMover>.Instance), clock);
        _db.Save(new AppSetting { Key = "ArchiveRoot", Value = _archive.Root });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _archive.Dispose();
        _downloads.Dispose();
        _db.Dispose();
    }

    // Copy mode, for the bulk import of an old archive: the original stays as the user's backup.
    [Fact]
    public async Task With_KeepSource_the_original_stays_where_it_was()
    {
        var content = TestPdf.Bytes("telefonica");
        var source = _downloads.WriteFile("scan 14-03.pdf", content);

        var result = await _import.ImportAsync(Request(source) with { KeepSource = true }, Ct);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SourceDeleted);
        Assert.Equal([TelefonicaPath], _archive.Files());
        Assert.Equal(content, File.ReadAllBytes(source));
    }

    [Fact]
    public async Task Moves_the_pdf_into_the_archive_and_adds_its_row()
    {
        var content = TestPdf.Bytes("telefonica");
        var source = _downloads.WriteFile("scan 14-03.pdf", content);

        var result = await _import.ImportAsync(Request(source), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.SourceDeleted);
        Assert.Equal([TelefonicaPath], _archive.Files());
        Assert.Equal(content, File.ReadAllBytes(_archive.Path(TelefonicaPath)));
        Assert.Empty(_downloads.Files());

        using var db = _db.CreateContext();
        var document = await db.Documents.Include(d => d.Partner).SingleAsync(Ct);
        Assert.Equal(Sha256Hasher.Compute(content), document.Sha256);
        Assert.Equal(Telefonica, document.Partner.Name);
        Assert.Equal("Telefonica-Espana", document.Partner.FolderName);
        Assert.Equal("A-2026/0123", document.DocNumber);
        Assert.Equal("A20260123", document.DocNumberNorm);
        Assert.Equal(new DateOnly(2026, 3, 14), document.IssueDate);
        Assert.Equal(DocType.Factura, document.Type);
        Assert.Equal(Direction.Recibida, document.Direction);
        Assert.Equal(TelefonicaPath, document.RelativePath);
        Assert.Equal("scan 14-03.pdf", document.OriginalName);
        Assert.Equal(content.Length, document.FileSizeBytes);
        Assert.Equal(Now, document.ImportedAt);
        Assert.Equal(Now, document.UpdatedAt);
        Assert.Null(document.DeletedAt);

        Assert.Equal(
            new DocumentListItem(document.Id, document.IssueDate, Telefonica, "A-2026/0123",
                DocType.Factura, Direction.Recibida, TelefonicaPath, IsPaid: false), // pending, as nothing said otherwise
            result.Value.Document);
        Assert.Equal((long)FileOperationState.Done, _db.Scalar("SELECT State FROM FileOperations"));
    }

    [Fact]
    public async Task An_issued_ticket_goes_to_Emitidas_with_its_suffix()
    {
        var source = _downloads.WriteFile("ticket.pdf", TestPdf.Bytes("ticket"));

        var result = await _import.ImportAsync(Request(source,
            partner: "Cliente Ejemplo S.L.", number: "T-88812", date: "2026-07-03",
            type: DocType.FacturaSimplificada, direction: Direction.Emitida), Ct);

        Assert.Equal(
            "2026/3T/Emitidas/Cliente-Ejemplo/2026-07-03_Cliente-Ejemplo_T-88812_Ticket.pdf",
            result.Value.Document.RelativePath);
        Assert.Equal([result.Value.Document.RelativePath], _archive.Files());
    }

    [Fact]
    public async Task A_document_already_paid_is_saved_as_paid()
    {
        var source = _downloads.WriteFile("pagada.pdf", TestPdf.Bytes("pagada"));

        var result = await _import.ImportAsync(Request(source) with { IsPaid = true }, Ct);

        Assert.True(result.Value.Document.IsPaid);
        Assert.Equal(1L, _db.Scalar("SELECT IsPaid FROM Documents"));
    }

    [Fact]
    public async Task An_empty_file_is_Empty()
    {
        var source = _downloads.WriteFile("empty.pdf", []);

        await AssertNotImportedAsync(source, ErrorCode.Empty);
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_is_NotPdf()
    {
        var source = _downloads.WriteFile("photo.pdf", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);

        await AssertNotImportedAsync(source, ErrorCode.NotPdf);
    }

    [Fact]
    public async Task A_file_that_is_gone_is_FileMissing()
    {
        var result = await _import.ImportAsync(Request(_downloads.Path("gone.pdf")), Ct);

        Assert.Equal(ErrorCode.FileMissing, result.Error);
        AssertNothingInTheArchive();
    }

    [Fact]
    public async Task A_file_locked_by_another_program_is_FileLocked_and_stays_where_it_is()
    {
        var source = _downloads.WriteFile("locked.pdf", TestPdf.Bytes("locked"));
        using (File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(ErrorCode.FileLocked, (await _import.ImportAsync(Request(source), Ct)).Error);

        Assert.True(File.Exists(source));
        AssertNothingInTheArchive();
    }

    [Fact]
    public async Task The_same_file_twice_is_AlreadyArchived_with_the_existing_id()
    {
        var content = TestPdf.Bytes("twice");
        var first = await _import.ImportAsync(Request(_downloads.WriteFile("a.pdf", content)), Ct);
        var copy = _downloads.WriteFile("copy of a.pdf", content);

        var second = await _import.ImportAsync(Request(copy, number: "OTHER-1"), Ct);

        Assert.Equal(ErrorCode.AlreadyArchived, second.Error);
        Assert.Equal(first.Value.Document.Id.ToString(CultureInfo.InvariantCulture), second.Detail);
        Assert.True(File.Exists(copy));
        Assert.Single(_archive.Files());
    }

    [Fact]
    public async Task The_same_file_as_a_deleted_document_is_AlreadyInTrash()
    {
        var content = TestPdf.Bytes("trash");
        await _import.ImportAsync(Request(_downloads.WriteFile("a.pdf", content)), Ct);
        _db.Scalar("UPDATE Documents SET DeletedAt = '2026-03-16 10:00:00'");

        var again = await _import.ImportAsync(Request(_downloads.WriteFile("a.pdf", content)), Ct);

        Assert.Equal(ErrorCode.AlreadyInTrash, again.Error);
    }

    [Theory]
    [InlineData("A-2026/0123")]
    [InlineData("A 2026 0123")]
    [InlineData("Nº A-2026-0123")]
    public async Task The_same_partner_and_number_is_DuplicateNumber(string number)
    {
        await _import.ImportAsync(Request(_downloads.WriteFile("a.pdf", TestPdf.Bytes("a"))), Ct);
        var second = _downloads.WriteFile("b.pdf", TestPdf.Bytes("b"));

        var result = await _import.ImportAsync(Request(second, partner: "TELEFONICA ESPAÑA SA", number: number), Ct);

        Assert.Equal(ErrorCode.DuplicateNumber, result.Error);
        Assert.True(File.Exists(second));
        Assert.Single(_archive.Files());
    }

    [Fact]
    public async Task A_confirmed_duplicate_is_imported_with_a_copy_number()
    {
        await _import.ImportAsync(Request(_downloads.WriteFile("a.pdf", TestPdf.Bytes("a"))), Ct);
        var request = Request(_downloads.WriteFile("b.pdf", TestPdf.Bytes("b")));
        Assert.Equal(ErrorCode.DuplicateNumber, (await _import.ImportAsync(request, Ct)).Error);

        var confirmed = await _import.ImportAsync(request with { ConfirmDuplicate = true }, Ct);

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(
            "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123_2.pdf",
            confirmed.Value.Document.RelativePath);
        Assert.Equal(2, _archive.Files().Length);
    }

    [Fact]
    public async Task A_partner_name_with_nothing_left_is_InvalidPartnerName()
    {
        var source = _downloads.WriteFile("a.pdf", TestPdf.Bytes("a"));

        await AssertNotImportedAsync(source, ErrorCode.InvalidPartnerName, partner: "S.L.");
    }

    [Fact]
    public async Task A_missing_archive_folder_is_ArchiveUnavailable()
    {
        Directory.Delete(_archive.Root);
        var source = _downloads.WriteFile("a.pdf", TestPdf.Bytes("a"));

        var result = await _import.ImportAsync(Request(source), Ct);

        Assert.Equal(ErrorCode.ArchiveUnavailable, result.Error);
        Assert.Equal(_archive.Root, result.Detail);
        Assert.False(Directory.Exists(_archive.Root)); // not recreated, it may be on the wrong disk
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task An_original_that_cannot_be_deleted_is_only_a_warning()
    {
        var source = _downloads.WriteFile("a.pdf", TestPdf.Bytes("viewer"));
        Result<ImportResult> result;
        using (File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read)) // a PDF viewer has it open
            result = await _import.ImportAsync(Request(source), Ct);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SourceDeleted);
        Assert.Equal([TelefonicaPath], _archive.Files());
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Two_imports_for_the_same_new_partner_share_its_folder()
    {
        await _import.ImportAsync(Request(_downloads.WriteFile("a.pdf", TestPdf.Bytes("a")),
            partner: "Repsol S.A.", number: "1"), Ct);
        await _import.ImportAsync(Request(_downloads.WriteFile("b.pdf", TestPdf.Bytes("b")),
            partner: "REPSOL", number: "2"), Ct);

        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM Partners"));
        Assert.Equal(
            ["2026/1T/Recibidas/Repsol/2026-03-14_Repsol_1.pdf", "2026/1T/Recibidas/Repsol/2026-03-14_Repsol_2.pdf"],
            _archive.Files());
    }

    [Theory]
    [InlineData("scan.pdf", "Repsol", "1")]
    [InlineData(@"C:\scan.pdf", " ", "1")]
    [InlineData(@"C:\scan.pdf", "Repsol", "")]
    public async Task A_relative_path_or_a_blank_field_throws(string source, string partner, string number)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _import.ImportAsync(Request(source, partner: partner, number: number), Ct));
    }

    [Fact]
    public async Task Importing_before_the_archive_folder_is_set_throws()
    {
        _db.Scalar("DELETE FROM Settings");
        var source = _downloads.WriteFile("a.pdf", TestPdf.Bytes("a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _import.ImportAsync(Request(source), Ct));
    }

    [Fact]
    public async Task Check_passes_a_new_pdf_and_changes_nothing()
    {
        var source = _downloads.WriteFile("a.pdf", TestPdf.Bytes("new"));

        var check = await _import.CheckAsync(source, Ct);

        Assert.True(check.IsSuccess);
        Assert.True(File.Exists(source));
        AssertNothingInTheArchive();
    }

    [Fact]
    public async Task Check_finds_what_the_import_would_refuse()
    {
        Assert.Equal(ErrorCode.Empty, (await _import.CheckAsync(_downloads.WriteFile("empty.pdf", []), Ct)).Error);
        Assert.Equal(ErrorCode.NotPdf, (await _import.CheckAsync(_downloads.WriteFile("photo.pdf", [0xFF, 0xD8, 0xFF]), Ct)).Error);
        Assert.Equal(ErrorCode.FileMissing, (await _import.CheckAsync(_downloads.Path("gone.pdf"), Ct)).Error);
    }

    [Fact]
    public async Task Check_reports_an_archived_file_with_its_id_and_one_in_the_trash()
    {
        var content = TestPdf.Bytes("archived");
        var first = await _import.ImportAsync(Request(_downloads.WriteFile("a.pdf", content)), Ct);
        var copy = _downloads.WriteFile("copy of a.pdf", content);

        var archived = await _import.CheckAsync(copy, Ct);
        _db.Scalar("UPDATE Documents SET DeletedAt = '2026-03-16 10:00:00'");
        var trashed = await _import.CheckAsync(copy, Ct);

        Assert.Equal(ErrorCode.AlreadyArchived, archived.Error);
        Assert.Equal(first.Value.Document.Id.ToString(CultureInfo.InvariantCulture), archived.Detail);
        Assert.Equal(ErrorCode.AlreadyInTrash, trashed.Error);
    }

    private async Task AssertNotImportedAsync(string source, ErrorCode expected, string partner = Telefonica)
    {
        var result = await _import.ImportAsync(Request(source, partner: partner), Ct);

        Assert.Equal(expected, result.Error);
        Assert.True(File.Exists(source));
        AssertNothingInTheArchive();
    }

    private void AssertNothingInTheArchive()
    {
        Assert.Empty(_archive.Files());
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM Documents"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM Partners"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    private static ImportRequest Request(
        string source,
        string partner = Telefonica,
        string number = "A-2026/0123",
        string date = "2026-03-14",
        DocType type = DocType.Factura,
        Direction direction = Direction.Recibida) => new()
    {
        SourcePath = source,
        PartnerName = partner,
        DocNumber = number,
        IssueDate = DateOnly.Parse(date, CultureInfo.InvariantCulture),
        Type = type,
        Direction = direction,
    };
}
