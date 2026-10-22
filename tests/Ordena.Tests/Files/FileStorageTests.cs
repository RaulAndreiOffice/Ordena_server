using Ordena.Backend.Files;
using Ordena.Backend.Results;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Files;

// Real files in a temp folder: locks, collisions and failed writes behave as they do in the app.
public sealed class FileStorageTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FileStorage _storage = new(TimeSpan.FromMilliseconds(1));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Read_returns_the_whole_file()
    {
        var content = TestPdf.Bytes("read");
        var path = _folder.WriteFile("in/a.pdf", content);

        var result = await _storage.ReadAsync(path, Ct);

        Assert.Equal(content, result.Value);
    }

    [Theory]
    [InlineData("missing.pdf")]
    [InlineData("no-folder/missing.pdf")]
    public async Task Read_of_a_missing_file_fails_with_FileMissing(string name)
    {
        var result = await _storage.ReadAsync(_folder.Path(name), Ct);

        Assert.Equal(ErrorCode.FileMissing, result.Error);
    }

    [Fact]
    public async Task Read_of_a_file_locked_by_another_program_fails_with_FileLocked()
    {
        var path = _folder.WriteFile("a.pdf", TestPdf.Bytes("locked"));
        using var other = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await _storage.ReadAsync(path, Ct);

        Assert.Equal(ErrorCode.FileLocked, result.Error);
    }

    [Fact]
    public async Task Read_waits_for_a_lock_that_is_released()
    {
        var content = TestPdf.Bytes("released");
        var path = _folder.WriteFile("a.pdf", content);
        var storage = new FileStorage(TimeSpan.FromMilliseconds(20)); // waits 20, 40, 60… ms: 560 ms in total
        var other = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(20, Ct);
            other.Dispose();
        }, Ct);

        var result = await storage.ReadAsync(path, Ct);
        await release;

        Assert.Equal(content, result.Value);
    }

    [Fact]
    public async Task Write_creates_the_folders_and_leaves_only_the_target()
    {
        var content = TestPdf.Bytes("write");

        var result = await _storage.WriteVerifiedAsync(
            _folder.Path("2026", "1T", "a.pdf"), content, Sha256Hasher.Compute(content), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["2026/1T/a.pdf"], _folder.Files());
        Assert.Equal(content, File.ReadAllBytes(_folder.Path("2026", "1T", "a.pdf")));
    }

    [Fact]
    public async Task Write_never_overwrites_an_existing_file()
    {
        var existing = TestPdf.Bytes("existing");
        var target = _folder.WriteFile("a.pdf", existing);
        var content = TestPdf.Bytes("new");

        var result = await _storage.WriteVerifiedAsync(target, content, Sha256Hasher.Compute(content), Ct);

        Assert.Equal(ErrorCode.TargetExists, result.Error);
        Assert.Equal(["a.pdf"], _folder.Files());
        Assert.Equal(existing, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task Write_fails_when_the_copy_does_not_match_the_hash()
    {
        var content = TestPdf.Bytes("hash");

        var result = await _storage.WriteVerifiedAsync(_folder.Path("a.pdf"), content, new string('0', 64), Ct);

        Assert.Equal(ErrorCode.IoError, result.Error);
        Assert.Empty(_folder.Files());
    }

    [Fact]
    public async Task Write_fails_with_IoError_when_a_file_blocks_the_folder()
    {
        _folder.WriteFile("2026", [1]); // a file with the year folder's name
        var content = TestPdf.Bytes("blocked");

        var result = await _storage.WriteVerifiedAsync(
            _folder.Path("2026", "a.pdf"), content, Sha256Hasher.Compute(content), Ct);

        Assert.Equal(ErrorCode.IoError, result.Error);
        Assert.Equal(["2026"], _folder.Files());
    }

    [Fact]
    public async Task Write_replaces_a_tmp_file_left_by_a_crash()
    {
        _folder.WriteFile("a.pdf.tmp", [1, 2, 3]);
        var content = TestPdf.Bytes("after-crash");

        var result = await _storage.WriteVerifiedAsync(_folder.Path("a.pdf"), content, Sha256Hasher.Compute(content), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["a.pdf"], _folder.Files());
        Assert.Equal(content, File.ReadAllBytes(_folder.Path("a.pdf")));
    }

    [Fact]
    public async Task Move_creates_the_folders_and_moves_the_file()
    {
        var content = TestPdf.Bytes("move");
        var from = _folder.WriteFile("2026/1T/a.pdf", content);

        var result = await _storage.MoveAsync(from, _folder.Path("2026", "2T", "b.pdf"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["2026/2T/b.pdf"], _folder.Files());
        Assert.Equal(content, File.ReadAllBytes(_folder.Path("2026", "2T", "b.pdf")));
    }

    [Fact]
    public async Task Move_of_a_missing_file_fails_with_FileMissing()
    {
        var result = await _storage.MoveAsync(_folder.Path("gone.pdf"), _folder.Path("b.pdf"), Ct);

        Assert.Equal(ErrorCode.FileMissing, result.Error);
    }

    [Fact]
    public async Task Move_never_overwrites_an_existing_file()
    {
        var from = _folder.WriteFile("a.pdf", TestPdf.Bytes("a"));
        var to = _folder.WriteFile("b.pdf", TestPdf.Bytes("b"));

        var result = await _storage.MoveAsync(from, to, Ct);

        Assert.Equal(ErrorCode.TargetExists, result.Error);
        Assert.Equal(TestPdf.Bytes("a"), File.ReadAllBytes(from));
        Assert.Equal(TestPdf.Bytes("b"), File.ReadAllBytes(to));
    }

    [Fact]
    public async Task Move_of_a_file_open_in_another_program_fails_with_FileLocked()
    {
        var from = _folder.WriteFile("a.pdf", TestPdf.Bytes("open"));
        using var viewer = File.Open(from, FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = await _storage.MoveAsync(from, _folder.Path("b.pdf"), Ct);

        Assert.Equal(ErrorCode.FileLocked, result.Error);
        Assert.Equal(["a.pdf"], _folder.Files());
    }

    [Fact]
    public async Task Move_can_change_only_the_case_of_the_name()
    {
        var from = _folder.WriteFile("x_a-1.pdf", TestPdf.Bytes("case"));

        var result = await _storage.MoveAsync(from, _folder.Path("x_A-1.pdf"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["x_A-1.pdf"], _folder.Files());
    }

    [Fact]
    public void TryDeleteEmptyFolders_stops_at_the_first_folder_still_in_use()
    {
        _folder.WriteFile("2026/1T/Recibidas/Other/keep.pdf", [1]);
        Directory.CreateDirectory(_folder.Path("2026", "1T", "Recibidas", "Repsol"));

        _storage.TryDeleteEmptyFolders(_folder.Path("2026", "1T", "Recibidas", "Repsol"), _folder.Root);

        Assert.False(Directory.Exists(_folder.Path("2026", "1T", "Recibidas", "Repsol")));
        Assert.Equal(["2026/1T/Recibidas/Other/keep.pdf"], _folder.Files());
    }

    [Fact]
    public void TryDeleteEmptyFolders_never_removes_the_root()
    {
        Directory.CreateDirectory(_folder.Path("2026", "1T"));

        _storage.TryDeleteEmptyFolders(_folder.Path("2026", "1T"), _folder.Root);

        Assert.False(Directory.Exists(_folder.Path("2026")));
        Assert.True(Directory.Exists(_folder.Root));
    }

    [Fact]
    public void Free_space_and_length_come_from_the_disk()
    {
        var path = _folder.WriteFile("a.pdf", [1, 2, 3]);

        Assert.True(_storage.AvailableFreeSpace(_folder.Root) > 0);
        Assert.Null(_storage.AvailableFreeSpace(@"\\server\share\Facturas")); // a network share can't tell
        Assert.Equal(3, _storage.GetLength(path));
    }

    [Fact]
    public async Task TryDelete_removes_the_file()
    {
        var path = _folder.WriteFile("a.pdf", TestPdf.Bytes("delete"));

        Assert.True(await _storage.TryDeleteAsync(path, Ct));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("missing.pdf")]
    [InlineData("no-folder/missing.pdf")]
    public async Task TryDelete_of_a_missing_file_counts_as_deleted(string name)
    {
        Assert.True(await _storage.TryDeleteAsync(_folder.Path(name), Ct));
    }

    [Fact]
    public async Task TryDelete_fails_while_another_program_has_the_file_open()
    {
        // Like a PDF viewer: other programs may read the file, but not delete it.
        var path = _folder.WriteFile("a.pdf", TestPdf.Bytes("open"));
        using var viewer = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.False(await _storage.TryDeleteAsync(path, Ct));
        Assert.True(File.Exists(path));
    }
}
