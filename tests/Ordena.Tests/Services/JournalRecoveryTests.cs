using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

// The app "dies" in the middle of an import or a move (CrashingStorage), then recovery runs
// as it would at the next start. Afterwards the disk and the database must agree.
public sealed class JournalRecoveryTests : IDisposable
{
    private const string Telefonica = "Telefónica España, S.A.U.";
    private const string MarchPath = "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf";
    private const string AprilPath = "2026/2T/Recibidas/Telefonica-Espana/2026-04-01_Telefonica-Espana_A-2026-0123.pdf";

    private readonly SqliteTestDatabase _db = new();
    private readonly TempFolder _archive = new();
    private readonly TempFolder _downloads = new();
    private readonly CrashingStorage _crashing;
    private readonly ImportService _import;
    private readonly DocumentService _documents;
    private readonly JournalRecovery _recovery;

    public JournalRecoveryTests()
    {
        var clock = new FixedClock(TestData.Now);
        var storage = new FileStorage(TimeSpan.FromMilliseconds(1));
        _crashing = new CrashingStorage(storage);
        var resolver = new PartnerResolver(clock);
        var mover = new ArchiveMover(_crashing, clock, NullLogger<ArchiveMover>.Instance);
        _import = new ImportService(_db.UnitOfWorkFactory, _crashing, resolver, mover, clock);
        _documents = new DocumentService(_db.UnitOfWorkFactory, storage, resolver, mover, clock);
        _recovery = new JournalRecovery(_db.UnitOfWorkFactory, storage, NullLogger<JournalRecovery>.Instance);
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
    public async Task An_import_that_died_after_writing_the_file_is_completed()
    {
        var source = _downloads.WriteFile("a.pdf", TestPdf.Bytes("a"));
        _crashing.When = CrashingStorage.Crash.AfterWrite;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _import.ImportAsync(Request(source), Ct));

        var resolved = await _recovery.RecoverAsync(_archive.Root, Ct);

        Assert.Equal(1, resolved);
        Assert.Equal([MarchPath], _archive.Files());
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM Documents"));
        Assert.Equal((long)FileOperationState.Done, _db.Scalar("SELECT State FROM FileOperations"));
        Assert.True(File.Exists(source)); // never deleted; dropping it again says "Ya archivado"
    }

    [Fact]
    public async Task An_import_that_died_before_the_copy_was_complete_is_undone()
    {
        var source = _downloads.WriteFile("a.pdf", TestPdf.Bytes("a"));
        _crashing.When = CrashingStorage.Crash.BeforeRename;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _import.ImportAsync(Request(source), Ct));
        Assert.Equal([MarchPath + ".tmp"], _archive.Files());

        await _recovery.RecoverAsync(_archive.Root, Ct);

        Assert.Empty(_archive.Files());
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM Documents"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM FileOperations"));
        Assert.True(File.Exists(source));

        // As if it never happened: the same file imports normally.
        _crashing.When = CrashingStorage.Crash.None;
        Assert.True((await _import.ImportAsync(Request(source), Ct)).IsSuccess);
    }

    [Fact]
    public async Task An_import_whose_target_holds_another_file_is_undone_without_touching_that_file()
    {
        var source = _downloads.WriteFile("a.pdf", TestPdf.Bytes("a"));
        _crashing.When = CrashingStorage.Crash.BeforeRename;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _import.ImportAsync(Request(source), Ct));
        _archive.WriteFile(MarchPath, TestPdf.Bytes("someone else's file"));

        await _recovery.RecoverAsync(_archive.Root, Ct);

        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM Documents"));
        Assert.Equal(TestPdf.Bytes("someone else's file"), File.ReadAllBytes(_archive.Path(MarchPath)));
    }

    [Fact]
    public async Task An_edit_that_died_after_moving_the_file_is_rolled_back()
    {
        var id = await ImportAsync("a");
        _crashing.When = CrashingStorage.Crash.AfterMove;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _documents.EditAsync(Edit(id, "2026-04-01"), Ct));
        Assert.Equal([AprilPath], _archive.Files()); // the file has its new name, the row still the old one

        await _recovery.RecoverAsync(_archive.Root, Ct);

        Assert.Equal([MarchPath], _archive.Files());
        Assert.False(Directory.Exists(_archive.Path("2026", "2T")));
        var details = await _documents.GetAsync(id, Ct);
        Assert.Equal((new DateOnly(2026, 3, 14), MarchPath), (details.IssueDate, details.RelativePath));
        Assert.Equal(
            ((long)FileOperationState.Failed, "Interrupted; the file was moved back."),
            LastOperation());

        // The edit can simply be made again.
        _crashing.When = CrashingStorage.Crash.None;
        Assert.Equal(AprilPath, (await _documents.EditAsync(Edit(id, "2026-04-01"), Ct)).Value.RelativePath);
    }

    [Fact]
    public async Task A_delete_that_died_after_moving_the_file_is_rolled_back()
    {
        var id = await ImportAsync("a");
        _crashing.When = CrashingStorage.Crash.AfterMove;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _documents.DeleteAsync(id, Ct));

        await _recovery.RecoverAsync(_archive.Root, Ct);

        Assert.Equal([MarchPath], _archive.Files());
        Assert.Null((await _documents.GetAsync(id, Ct)).DeletedAt);
    }

    [Fact]
    public async Task A_move_that_died_before_the_file_moved_is_marked_failed()
    {
        var id = await ImportAsync("a");
        _db.Save(PendingMove(id, MarchPath, AprilPath));

        await _recovery.RecoverAsync(_archive.Root, Ct);

        Assert.Equal([MarchPath], _archive.Files());
        Assert.Equal(((long)FileOperationState.Failed, "Interrupted before the file moved."), LastOperation());
    }

    [Fact]
    public async Task A_move_whose_file_is_in_neither_place_is_marked_failed()
    {
        var id = await ImportAsync("a");
        File.Delete(_archive.Path(MarchPath));
        _db.Save(PendingMove(id, MarchPath, AprilPath));

        await _recovery.RecoverAsync(_archive.Root, Ct);

        Assert.Equal(
            ((long)FileOperationState.Failed, "Interrupted, and the file is in neither place."),
            LastOperation());
    }

    [Fact]
    public async Task A_move_that_cannot_be_undone_yet_stays_pending_for_the_next_try()
    {
        var id = await ImportAsync("a");
        _crashing.When = CrashingStorage.Crash.AfterMove;
        await Assert.ThrowsAsync<SimulatedCrashException>(() => _documents.EditAsync(Edit(id, "2026-04-01"), Ct));

        using (File.Open(_archive.Path(AprilPath), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(0, await _recovery.RecoverAsync(_archive.Root, Ct));
        Assert.Equal((long)FileOperationState.Pending, LastOperation().State);

        Assert.Equal(1, await _recovery.RecoverAsync(_archive.Root, Ct));
        Assert.Equal([MarchPath], _archive.Files());
    }

    [Fact]
    public async Task Finished_moves_are_left_alone()
    {
        await ImportAsync("a");

        Assert.Equal(0, await _recovery.RecoverAsync(_archive.Root, Ct));
        Assert.Equal((long)FileOperationState.Done, LastOperation().State);
    }

    private async Task<int> ImportAsync(string marker) =>
        (await _import.ImportAsync(Request(_downloads.WriteFile($"{marker}.pdf", TestPdf.Bytes(marker))), Ct))
        .Value.Document.Id;

    private (long State, string? Error) LastOperation() =>
        _db.Query("SELECT State, Error FROM FileOperations ORDER BY Id DESC LIMIT 1",
            r => (r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1))).Single();

    private static FileOperation PendingMove(int documentId, string fromPath, string toPath) => new()
    {
        DocumentId = documentId,
        FromPath = fromPath,
        ToPath = toPath,
        State = FileOperationState.Pending,
        CreatedAt = TestData.Now,
    };

    private static ImportRequest Request(string source) => new()
    {
        SourcePath = source,
        PartnerName = Telefonica,
        DocNumber = "A-2026/0123",
        IssueDate = new DateOnly(2026, 3, 14),
        Type = DocType.Factura,
        Direction = Direction.Recibida,
    };

    private static EditDocumentRequest Edit(int id, string date) => new()
    {
        Id = id,
        PartnerName = Telefonica,
        DocNumber = "A-2026/0123",
        IssueDate = DateOnly.Parse(date, CultureInfo.InvariantCulture),
        Type = DocType.Factura,
        Direction = Direction.Recibida,
    };
}
