using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

// Documents are put in place by the real ImportService, then edited, deleted and restored.
public sealed class DocumentServiceTests : IDisposable
{
    private const string Telefonica = "Telefónica España, S.A.U.";
    private const string TelefonicaFolder = "2026/1T/Recibidas/Telefonica-Espana/";
    private const string TelefonicaPath = TelefonicaFolder + "2026-03-14_Telefonica-Espana_A-2026-0123.pdf";
    private const string TrashPath = "_Papelera/" + TelefonicaPath;

    private static readonly DateTime ImportedAt = new(2026, 3, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 5, 2, 17, 45, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _db = new();
    private readonly TempFolder _archive = new();
    private readonly TempFolder _downloads = new();
    private readonly FixedClock _clock = new(ImportedAt);
    private readonly ImportService _import;
    private readonly DocumentService _documents;
    private readonly SearchService _search;

    public DocumentServiceTests()
    {
        var storage = new FileStorage(TimeSpan.FromMilliseconds(1));
        var resolver = new PartnerResolver(_clock);
        var mover = new ArchiveMover(storage, _clock, NullLogger<ArchiveMover>.Instance);
        _import = new ImportService(_db.UnitOfWorkFactory, storage, resolver, mover, _clock);
        _documents = new DocumentService(_db.UnitOfWorkFactory, storage, resolver, mover, _clock);
        _search = new SearchService(_db.UnitOfWorkFactory);
        _db.Save(new AppSetting { Key = "ArchiveRoot", Value = _archive.Root });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _archive.Dispose();
        _downloads.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task Get_returns_every_field()
    {
        var id = await ImportAsync("a");

        var details = await _documents.GetAsync(id, Ct);

        Assert.Equal(
            new DocumentDetails(id, Telefonica, "A-2026/0123", new DateOnly(2026, 3, 14), DocType.Factura,
                Direction.Recibida, TelefonicaPath, "a.pdf", ImportedAt, ImportedAt, null, IsPaid: false),
            details);
    }

    [Fact]
    public async Task An_unknown_id_throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _documents.GetAsync(999, Ct));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _documents.DeleteAsync(999, Ct));
    }

    [Fact]
    public async Task A_date_in_another_quarter_moves_the_file_and_saves_the_row()
    {
        var id = await ImportAsync("a");
        _clock.UtcNow = Later;

        var result = await _documents.EditAsync(Edit(id, date: "2026-04-01"), Ct);

        const string april = "2026/2T/Recibidas/Telefonica-Espana/2026-04-01_Telefonica-Espana_A-2026-0123.pdf";
        Assert.Equal(april, result.Value.RelativePath);
        Assert.Equal([april], _archive.Files());
        Assert.False(Directory.Exists(_archive.Path("2026", "1T"))); // left empty, so removed
        var details = await _documents.GetAsync(id, Ct);
        Assert.Equal(new DateOnly(2026, 4, 1), details.IssueDate);
        Assert.Equal(april, details.RelativePath);
        Assert.Equal(Later, details.UpdatedAt);
    }

    [Fact]
    public async Task A_new_partner_gets_its_own_folder()
    {
        var id = await ImportAsync("a");

        var result = await _documents.EditAsync(Edit(id, partner: "Repsol S.A."), Ct);

        Assert.Equal("Repsol S.A.", result.Value.PartnerName);
        Assert.Equal(["2026/1T/Recibidas/Repsol/2026-03-14_Repsol_A-2026-0123.pdf"], _archive.Files());
        Assert.Equal(2L, _db.Scalar("SELECT COUNT(*) FROM Partners"));
    }

    [Fact]
    public async Task An_existing_partner_is_reused()
    {
        var id = await ImportAsync("a");
        await ImportAsync("b", partner: "Repsol S.A.", number: "R-1");

        await _documents.EditAsync(Edit(id, partner: "REPSOL"), Ct);

        Assert.Equal(2L, _db.Scalar("SELECT COUNT(*) FROM Partners"));
        Assert.Equal(
            ["2026/1T/Recibidas/Repsol/2026-03-14_Repsol_A-2026-0123.pdf", "2026/1T/Recibidas/Repsol/2026-03-14_Repsol_R-1.pdf"],
            _archive.Files());
    }

    [Fact]
    public async Task A_new_type_changes_the_suffix()
    {
        var id = await ImportAsync("a");

        await _documents.EditAsync(Edit(id, type: DocType.FacturaRectificativa), Ct);

        Assert.Equal([TelefonicaFolder + "2026-03-14_Telefonica-Espana_A-2026-0123_Rectif.pdf"], _archive.Files());
    }

    [Fact]
    public async Task A_number_that_only_changes_case_renames_the_file()
    {
        var id = await ImportAsync("a", number: "a-2026-0123");

        await _documents.EditAsync(Edit(id, number: "A-2026-0123"), Ct);

        Assert.Equal([TelefonicaPath], _archive.Files());
    }

    [Fact]
    public async Task An_edit_that_keeps_the_file_name_moves_nothing()
    {
        var id = await ImportAsync("a");

        // Its own number is not a duplicate, and "A 2026 0123" gives the same file name.
        var result = await _documents.EditAsync(Edit(id, number: "A 2026 0123"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("A 2026 0123", (await _documents.GetAsync(id, Ct)).DocNumber);
        Assert.Equal([TelefonicaPath], _archive.Files());
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM FileOperations")); // only the import's
    }

    [Fact]
    public async Task Another_documents_number_is_DuplicateNumber_until_confirmed()
    {
        await ImportAsync("a");
        var id = await ImportAsync("b", number: "B-1");
        var request = Edit(id, number: "A-2026/0123");

        var warned = await _documents.EditAsync(request, Ct);
        var confirmed = await _documents.EditAsync(request with { ConfirmDuplicate = true }, Ct);

        Assert.Equal(ErrorCode.DuplicateNumber, warned.Error);
        Assert.Equal(TelefonicaFolder + "2026-03-14_Telefonica-Espana_A-2026-0123_2.pdf", confirmed.Value.RelativePath);
    }

    [Fact]
    public async Task An_edit_whose_file_is_missing_changes_nothing()
    {
        var id = await ImportAsync("a");
        File.Delete(_archive.Path(TelefonicaPath)); // moved away by hand

        var result = await _documents.EditAsync(Edit(id, partner: "Repsol S.A.", date: "2026-04-01"), Ct);

        Assert.Equal(ErrorCode.FileMissing, result.Error);
        var details = await _documents.GetAsync(id, Ct);
        Assert.Equal((Telefonica, new DateOnly(2026, 3, 14), TelefonicaPath),
            (details.PartnerName, details.IssueDate, details.RelativePath));
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM Partners")); // Repsol was created for the edit, so removed
        Assert.Equal((long)FileOperationState.Failed, _db.Scalar("SELECT State FROM FileOperations ORDER BY Id DESC"));
    }

    [Fact]
    public async Task Delete_moves_the_file_to_the_trash_and_hides_the_document()
    {
        var id = await ImportAsync("a");
        _clock.UtcNow = Later;

        var result = await _documents.DeleteAsync(id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([TrashPath], _archive.Files());
        Assert.Equal(Later, (await _documents.GetAsync(id, Ct)).DeletedAt);
        Assert.Equal(0, (await _search.SearchAsync(new SearchQuery(), Ct)).TotalCount);
        Assert.Equal((long)FileOperationState.Done, _db.Scalar("SELECT State FROM FileOperations ORDER BY Id DESC"));
    }

    [Fact]
    public async Task A_second_document_deleted_from_the_same_path_gets_a_copy_number_in_the_trash()
    {
        await _documents.DeleteAsync(await ImportAsync("a"), Ct);
        var second = await ImportAsync("b"); // the same name is free again

        await _documents.DeleteAsync(second, Ct);

        Assert.Equal([TrashPath, TrashPath.Replace(".pdf", "_2.pdf")], _archive.Files());
    }

    [Fact]
    public async Task Restore_moves_the_file_back_and_shows_the_document_again()
    {
        var id = await ImportAsync("a");
        await _documents.DeleteAsync(id, Ct);

        var result = await _documents.RestoreAsync(id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([TelefonicaPath], _archive.Files());
        Assert.False(Directory.Exists(_archive.Path("_Papelera")));
        Assert.Null((await _documents.GetAsync(id, Ct)).DeletedAt);
        Assert.Equal(1, (await _search.SearchAsync(new SearchQuery(), Ct)).TotalCount);
    }

    [Fact]
    public async Task Restore_gets_a_copy_number_when_the_name_was_taken_meanwhile()
    {
        var id = await ImportAsync("a");
        await _documents.DeleteAsync(id, Ct);
        await ImportAsync("b");

        await _documents.RestoreAsync(id, Ct);

        Assert.Equal(TelefonicaPath.Replace(".pdf", "_2.pdf"), (await _documents.GetAsync(id, Ct)).RelativePath);
    }

    [Fact]
    public async Task The_trash_lists_deleted_documents_newest_first()
    {
        var first = await ImportAsync("a", number: "1");
        await ImportAsync("b", number: "2");
        var third = await ImportAsync("c", number: "3");
        await _documents.DeleteAsync(first, Ct);
        _clock.UtcNow = Later;
        await _documents.DeleteAsync(third, Ct);

        var trash = await _documents.ListTrashAsync(Ct);

        Assert.Equal([third, first], trash.Select(d => d.Id));
    }

    [Fact]
    public async Task GetFullPath_gives_the_file_on_disk()
    {
        var id = await ImportAsync("a");

        var path = await _documents.GetFullPathAsync(id, Ct);

        Assert.Equal(_archive.Path("2026", "1T", "Recibidas", "Telefonica-Espana",
            "2026-03-14_Telefonica-Espana_A-2026-0123.pdf"), path.Value);
    }

    [Fact]
    public async Task GetFullPath_of_a_missing_file_is_FileMissing()
    {
        var id = await ImportAsync("a");
        var expected = _archive.Path("2026", "1T", "Recibidas", "Telefonica-Espana",
            "2026-03-14_Telefonica-Espana_A-2026-0123.pdf");
        File.Delete(expected);

        var path = await _documents.GetFullPathAsync(id, Ct);

        Assert.Equal(ErrorCode.FileMissing, path.Error);
        Assert.Equal(expected, path.Detail);
    }

    [Fact]
    public async Task Relink_copies_the_same_file_from_outside_the_archive_and_leaves_it_there()
    {
        var id = await ImportAsync("a");
        File.Delete(_archive.Path(TelefonicaPath));
        var backupCopy = _downloads.WriteFile("usb/a.pdf", TestPdf.Bytes("a"));

        var result = await _documents.RelinkAsync(id, backupCopy, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([TelefonicaPath], _archive.Files());
        Assert.True(File.Exists(backupCopy));
        Assert.True((await _documents.GetFullPathAsync(id, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Relink_moves_a_file_found_elsewhere_in_the_archive()
    {
        var id = await ImportAsync("a");
        var misplaced = _archive.Path("2026", "Otros", "a.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(misplaced)!);
        File.Move(_archive.Path(TelefonicaPath), misplaced);

        var result = await _documents.RelinkAsync(id, misplaced, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([TelefonicaPath], _archive.Files());
    }

    [Fact]
    public async Task Relink_refuses_a_file_with_other_content()
    {
        var id = await ImportAsync("a");
        File.Delete(_archive.Path(TelefonicaPath));
        var other = _downloads.WriteFile("other.pdf", TestPdf.Bytes("another invoice"));

        var result = await _documents.RelinkAsync(id, other, Ct);

        Assert.Equal(ErrorCode.NotSameFile, result.Error);
        Assert.Empty(_archive.Files());
        Assert.True(File.Exists(other));
    }

    [Fact]
    public async Task Relink_with_a_relative_path_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _documents.RelinkAsync(1, "a.pdf", Ct));
    }

    [Fact]
    public async Task A_missing_archive_folder_is_ArchiveUnavailable()
    {
        var id = await ImportAsync("a");
        Directory.Delete(_archive.Root, recursive: true);

        Assert.Equal(ErrorCode.ArchiveUnavailable, (await _documents.DeleteAsync(id, Ct)).Error);
        Assert.Equal(ErrorCode.ArchiveUnavailable, (await _documents.GetFullPathAsync(id, Ct)).Error);
    }

    [Fact]
    public async Task Using_a_document_in_the_wrong_state_throws()
    {
        var active = await ImportAsync("a", number: "1");
        var deleted = await ImportAsync("b", number: "2");
        await _documents.DeleteAsync(deleted, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _documents.EditAsync(Edit(deleted), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _documents.SetPaidAsync(deleted, true, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _documents.DeleteAsync(deleted, Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _documents.RestoreAsync(active, Ct));
    }

    [Theory]
    [InlineData(" ", "1")]
    [InlineData("Repsol", "")]
    public async Task A_blank_partner_or_number_throws(string partner, string number)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _documents.EditAsync(Edit(1, partner: partner, number: number), Ct));
    }

    // «Marcar como pagada»: the status isn't in the file name, so only the row changes; and with no file to
    // touch, it works with the archive's disk unplugged too.
    [Fact]
    public async Task Marking_a_document_paid_changes_only_its_row()
    {
        var id = await ImportAsync("a");
        _clock.UtcNow = Later;
        var unplugged = _archive.Root + "-unplugged";
        Directory.Move(_archive.Root, unplugged);
        try
        {
            Assert.True((await _documents.SetPaidAsync(id, true, Ct)).IsSuccess);
        }
        finally
        {
            Directory.Move(unplugged, _archive.Root);
        }

        var details = await _documents.GetAsync(id, Ct);
        Assert.Equal((true, Later, TelefonicaPath), (details.IsPaid, details.UpdatedAt, details.RelativePath));
        Assert.Equal([TelefonicaPath], _archive.Files());
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM FileOperations")); // only the import's

        await _documents.SetPaidAsync(id, false, Ct);
        Assert.False((await _documents.GetAsync(id, Ct)).IsPaid);
    }

    [Fact]
    public async Task An_edit_that_only_marks_it_paid_moves_nothing()
    {
        var id = await ImportAsync("a");

        var result = await _documents.EditAsync(Edit(id, isPaid: true), Ct);

        Assert.True(result.Value.IsPaid);
        Assert.True((await _documents.GetAsync(id, Ct)).IsPaid);
        Assert.Equal([TelefonicaPath], _archive.Files());
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    private async Task<int> ImportAsync(
        string marker, string partner = Telefonica, string number = "A-2026/0123", string date = "2026-03-14")
    {
        var result = await _import.ImportAsync(new ImportRequest
        {
            SourcePath = _downloads.WriteFile($"{marker}.pdf", TestPdf.Bytes(marker)),
            PartnerName = partner,
            DocNumber = number,
            IssueDate = DateOnly.Parse(date, CultureInfo.InvariantCulture),
            Type = DocType.Factura,
            Direction = Direction.Recibida,
            ConfirmDuplicate = true,
        }, Ct);
        return result.Value.Document.Id;
    }

    private static EditDocumentRequest Edit(
        int id,
        string partner = Telefonica,
        string number = "A-2026/0123",
        string date = "2026-03-14",
        DocType type = DocType.Factura,
        Direction direction = Direction.Recibida,
        bool isPaid = false) => new()
    {
        Id = id,
        PartnerName = partner,
        DocNumber = number,
        IssueDate = DateOnly.Parse(date, CultureInfo.InvariantCulture),
        Type = type,
        Direction = direction,
        IsPaid = isPaid,
    };
}
