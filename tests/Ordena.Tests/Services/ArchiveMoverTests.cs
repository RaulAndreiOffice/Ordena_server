using Microsoft.Extensions.Logging.Abstractions;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

public sealed class ArchiveMoverTests : IDisposable
{
    private const string Source = @"C:\Users\me\Downloads\scan.pdf";
    private const string Folder = "2026/1T/Recibidas/Telefonica-Espana/";
    private const string FileName = "2026-03-14_Telefonica-Espana_A-2026-0123";
    private const string Expected = Folder + FileName + ".pdf";
    private const string April = "2026/2T/Recibidas/Telefonica-Espana/2026-04-01_Telefonica-Espana_A-2026-0123.pdf";

    private readonly SqliteTestDatabase _db = new();
    private readonly TempFolder _archive = new();
    private readonly FileStorage _storage = new(TimeSpan.FromMilliseconds(1));
    private readonly FixedClock _clock = new(TestData.Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _archive.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task Writes_the_file_and_marks_the_journal_row_done()
    {
        _db.Save(TestData.NewPartner());
        var content = TestPdf.Bytes("ok");

        var result = await ImportAsync(content);

        Assert.True(result.IsSuccess);
        Assert.Equal([Expected], _archive.Files());
        Assert.Equal(content, File.ReadAllBytes(_archive.Path(Expected)));
        Assert.Equal(Expected, _db.Scalar("SELECT RelativePath FROM Documents"));
        Assert.Equal(
            [(Source, Expected, (long)FileOperationState.Done)],
            _db.Query("SELECT FromPath, ToPath, State FROM FileOperations",
                r => (r.GetString(0), r.GetString(1), r.GetInt64(2))));
    }

    // That order is what makes a crash in the middle recoverable.
    [Fact]
    public async Task The_document_and_a_pending_journal_row_are_saved_before_the_file_is_written()
    {
        List<long> statesDuringWrite = [];
        var probe = new ProbeStorage(_storage, onWrite: () =>
            statesDuringWrite.AddRange(_db.Query("SELECT State FROM FileOperations", r => r.GetInt64(0))));

        await ImportAsync(TestPdf.Bytes("probe"), storage: probe);

        Assert.Equal([(long)FileOperationState.Pending], statesDuringWrite);
    }

    [Fact]
    public async Task A_name_taken_on_disk_gets_a_copy_number()
    {
        _archive.WriteFile(Expected, [1]); // a file the app doesn't know about

        await ImportAsync(TestPdf.Bytes("disk"));

        Assert.Equal([Expected, Folder + FileName + "_2.pdf"], _archive.Files());
        Assert.Equal(Folder + FileName + "_2.pdf", _db.Scalar("SELECT RelativePath FROM Documents"));
    }

    [Fact]
    public async Task A_name_taken_only_in_the_database_gets_a_copy_number()
    {
        // A document whose file has gone missing still owns its path.
        var partner = _db.Save(TestData.NewPartner());
        _db.Save(TestData.NewDocument(partner.Id, relativePath: Expected));

        var result = await ImportAsync(TestPdf.Bytes("db"));

        Assert.True(result.IsSuccess);
        Assert.Equal([Folder + FileName + "_2.pdf"], _archive.Files());
    }

    [Fact]
    public async Task Copy_numbers_skip_names_taken_on_disk_and_in_the_database()
    {
        var partner = _db.Save(TestData.NewPartner());
        _archive.WriteFile(Expected, [1]);
        _db.Save(TestData.NewDocument(partner.Id, relativePath: Folder + FileName + "_2.pdf"));

        await ImportAsync(TestPdf.Bytes("both"));

        Assert.Equal([Expected, Folder + FileName + "_3.pdf"], _archive.Files());
    }

    [Fact]
    public async Task A_failed_write_leaves_nothing_behind()
    {
        _archive.WriteFile("2026", [1]); // a file where the year folder should be

        var result = await ImportAsync(TestPdf.Bytes("fail"), partnerName: "Repsol S.A.");

        Assert.Equal(ErrorCode.IoError, result.Error);
        Assert.Equal(["2026"], _archive.Files());
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM Documents"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM FileOperations"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM Partners")); // created for this import, so removed
    }

    [Fact]
    public async Task A_failed_write_keeps_a_partner_that_already_existed()
    {
        _db.Save(TestData.NewPartner());
        _archive.WriteFile("2026", [1]);

        var result = await ImportAsync(TestPdf.Bytes("fail"));

        Assert.False(result.IsSuccess);
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM Partners"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM Documents"));
    }

    // Like ImportService: the partner is resolved in the same unit of work, then the document is archived.
    private async Task<Result> ImportAsync(
        byte[] content, string partnerName = "Telefónica España, S.A.U.", IFileStorage? storage = null)
    {
        await using var uow = _db.UnitOfWorkFactory.Create();
        var partner = await new PartnerResolver(_clock).GetOrCreateAsync(uow, partnerName, Ct);
        var document = TestData.NewDocument(0, sha256: Sha256Hasher.Compute(content), docNumber: "A-2026/0123");
        document.Partner = partner.Value;

        var mover = new ArchiveMover(storage ?? _storage, _clock, NullLogger<ArchiveMover>.Instance);
        return await mover.ImportAsync(uow, document, Source, content, _archive.Root, Ct);
    }

    [Fact]
    public async Task Move_moves_the_file_applies_the_changes_and_marks_the_row_done()
    {
        var document = SaveArchived(Expected);

        var result = await MoveAsync(document.Id, April, d => d.IssueDate = new DateOnly(2026, 4, 1));

        Assert.True(result.IsSuccess);
        Assert.Equal([April], _archive.Files());
        Assert.False(Directory.Exists(_archive.Path("2026", "1T"))); // left empty, so removed
        Assert.Equal(April, _db.Scalar("SELECT RelativePath FROM Documents"));
        Assert.Equal("2026-04-01", _db.Scalar("SELECT IssueDate FROM Documents"));
        Assert.Equal(
            [(Expected, April, (long)FileOperationState.Done)],
            _db.Query("SELECT FromPath, ToPath, State FROM FileOperations",
                r => (r.GetString(0), r.GetString(1), r.GetInt64(2))));
    }

    // Changes saved only after the move: a crash in between can always be rolled back.
    [Fact]
    public async Task Move_saves_a_pending_row_but_not_the_changes_before_the_file_moves()
    {
        var document = SaveArchived(Expected);
        List<(long, string)> duringMove = [];
        var probe = new ProbeStorage(_storage, onMove: () => duringMove.AddRange(_db.Query(
            "SELECT o.State, d.IssueDate FROM FileOperations o JOIN Documents d ON d.Id = o.DocumentId",
            r => (r.GetInt64(0), r.GetString(1)))));

        await MoveAsync(document.Id, April, d => d.IssueDate = new DateOnly(2026, 4, 1), probe);

        Assert.Equal([((long)FileOperationState.Pending, "2026-03-14")], duringMove);
    }

    [Fact]
    public async Task Move_to_a_taken_name_gets_a_copy_number()
    {
        var document = SaveArchived(Expected);
        _archive.WriteFile(April, [1]);

        await MoveAsync(document.Id, April, _ => { });

        Assert.Equal(April.Replace(".pdf", "_2.pdf"), _db.Scalar("SELECT RelativePath FROM Documents"));
    }

    [Fact]
    public async Task Move_to_the_same_path_saves_the_changes_without_touching_the_file()
    {
        var document = SaveArchived(Expected);

        var result = await MoveAsync(document.Id, Expected, d => d.DocNumber = "A 2026 0123");

        Assert.True(result.IsSuccess);
        Assert.Equal([Expected], _archive.Files());
        Assert.Equal("A 2026 0123", _db.Scalar("SELECT DocNumber FROM Documents"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task A_document_keeps_its_copy_number_while_the_standard_name_is_taken()
    {
        var document = SaveArchived(Folder + FileName + "_2.pdf");
        _archive.WriteFile(Expected, [1]);

        await MoveAsync(document.Id, Expected, _ => { });

        Assert.Equal(Folder + FileName + "_2.pdf", _db.Scalar("SELECT RelativePath FROM Documents"));
        Assert.Equal(0L, _db.Scalar("SELECT COUNT(*) FROM FileOperations"));
    }

    [Fact]
    public async Task Move_can_change_only_the_case_of_the_name()
    {
        var document = SaveArchived(Folder + "2026-03-14_Telefonica-Espana_a-2026-0123.pdf");

        var result = await MoveAsync(document.Id, Expected, _ => { });

        Assert.True(result.IsSuccess);
        Assert.Equal([Expected], _archive.Files());
        Assert.Equal(Expected, _db.Scalar("SELECT RelativePath FROM Documents"));
    }

    [Fact]
    public async Task A_failed_move_marks_the_row_failed_and_leaves_the_document_as_it_was()
    {
        var document = SaveArchived(Expected, withFile: false); // moved away by hand

        var result = await MoveAsync(document.Id, April, d => d.IssueDate = new DateOnly(2026, 4, 1));

        Assert.Equal(ErrorCode.FileMissing, result.Error);
        Assert.Equal(Expected, _db.Scalar("SELECT RelativePath FROM Documents"));
        Assert.Equal("2026-03-14", _db.Scalar("SELECT IssueDate FROM Documents"));
        var (state, error) = Assert.Single(_db.Query("SELECT State, Error FROM FileOperations",
            r => (r.GetInt64(0), r.GetString(1))));
        Assert.Equal((long)FileOperationState.Failed, state);
        Assert.StartsWith("FileMissing: ", error);
    }

    // A saved document; its file is in the archive unless withFile is false.
    private Document SaveArchived(string relativePath, bool withFile = true)
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = _db.Save(TestData.NewDocument(partner.Id, relativePath: relativePath));
        if (withFile)
            _archive.WriteFile(relativePath, TestPdf.Bytes(relativePath));
        return document;
    }

    // Like DocumentService: the document is loaded tracked in the unit of work that moves it.
    private async Task<Result> MoveAsync(
        int documentId, string newRelativePath, Action<Document> applyChanges, IFileStorage? storage = null)
    {
        await using var uow = _db.UnitOfWorkFactory.Create();
        var document = await uow.Documents.GetByIdAsync(documentId, Ct);

        var mover = new ArchiveMover(storage ?? _storage, _clock, NullLogger<ArchiveMover>.Instance);
        return await mover.MoveAsync(uow, document!, newRelativePath, _archive.Root, applyChanges, Ct);
    }

    // The real storage, plus a look at the database at the moment the file is written or moved.
    private sealed class ProbeStorage(IFileStorage inner, Action? onWrite = null, Action? onMove = null) : IFileStorage
    {
        public Task<Result> WriteVerifiedAsync(
            string targetPath, ReadOnlyMemory<byte> content, string sha256, CancellationToken ct)
        {
            onWrite?.Invoke();
            return inner.WriteVerifiedAsync(targetPath, content, sha256, ct);
        }

        public Task<Result> MoveAsync(string fromPath, string toPath, CancellationToken ct)
        {
            onMove?.Invoke();
            return inner.MoveAsync(fromPath, toPath, ct);
        }

        public Task<Result<byte[]>> ReadAsync(string path, CancellationToken ct) => inner.ReadAsync(path, ct);
        public Task<bool> TryDeleteAsync(string path, CancellationToken ct) => inner.TryDeleteAsync(path, ct);
        public void TryDeleteEmptyFolders(string folder, string stopAt) => inner.TryDeleteEmptyFolders(folder, stopAt);
        public Result CreateFolder(string path) => inner.CreateFolder(path);
    public IReadOnlyList<string> ListFiles(string folder) => inner.ListFiles(folder);
        public void TryEmptyFolder(string folder) => inner.TryEmptyFolder(folder);
        public long? AvailableFreeSpace(string path) => inner.AvailableFreeSpace(path);
        public long GetLength(string path) => inner.GetLength(path);
        public DateTime GetLastWriteTime(string path) => inner.GetLastWriteTime(path);
        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
    }
}
