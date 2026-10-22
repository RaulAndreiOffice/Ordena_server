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
        var probe = new ProbeStorage(_storage, () =>
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

        var mover = new ArchiveMover(storage ?? _storage, _clock);
        return await mover.ImportAsync(uow, document, Source, content, _archive.Root, Ct);
    }

    // The real storage, plus a look at the database at the moment the file is written.
    private sealed class ProbeStorage(IFileStorage inner, Action onWrite) : IFileStorage
    {
        public Task<Result> WriteVerifiedAsync(
            string targetPath, ReadOnlyMemory<byte> content, string sha256, CancellationToken ct)
        {
            onWrite();
            return inner.WriteVerifiedAsync(targetPath, content, sha256, ct);
        }

        public Task<Result<byte[]>> ReadAsync(string path, CancellationToken ct) => inner.ReadAsync(path, ct);
        public Task<bool> TryDeleteAsync(string path, CancellationToken ct) => inner.TryDeleteAsync(path, ct);
        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
    }
}
