using Ordena.Backend.Results;

namespace Ordena.Backend.Files;

internal sealed class FileStorage : IFileStorage
{
    // 8 attempts with 100, 200, … 700 ms between them: about 2.8 s before giving up.
    private const int Attempts = 8;

    // Windows error codes, in the low 16 bits of IOException.HResult.
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;

    private readonly TimeSpan _retryDelay;

    public FileStorage() : this(TimeSpan.FromMilliseconds(100)) { }

    // Tests pass a short delay. Internal, so DI only sees the parameterless constructor.
    internal FileStorage(TimeSpan retryDelay) => _retryDelay = retryDelay;

    public async Task<Result<byte[]>> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            var content = await RetryWhileLockedAsync(() => File.ReadAllBytesAsync(path, ct), ct).ConfigureAwait(false);
            return Result.Ok(content);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException)
        {
            return Result.Fail<byte[]>(ErrorCode.FileMissing, path);
        }
        catch (IOException ex) when (IsLocked(ex))
        {
            return Result.Fail<byte[]>(ErrorCode.FileLocked, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<byte[]>(ErrorCode.IoError, ex.Message);
        }
    }

    public async Task<Result> WriteVerifiedAsync(
        string targetPath, ReadOnlyMemory<byte> content, string sha256, CancellationToken ct)
    {
        // A fixed name next to the target, so startup recovery knows what to clean up.
        var tmpPath = targetPath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            // FileMode.Create also replaces a tmp file left behind by a crash.
            await using (var stream = new FileStream(
                tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await stream.WriteAsync(content, ct).ConfigureAwait(false);
                // On the disk, not just in the cache, before the database says the move is done.
                stream.Flush(flushToDisk: true);
            }

            if (Sha256Hasher.ComputeFile(tmpPath) != sha256)
            {
                DeleteQuietly(tmpPath);
                return Result.Fail(ErrorCode.IoError, $"The copy written to '{tmpPath}' doesn't match the original.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(tmpPath);
            return Result.Fail(ErrorCode.IoError, ex.Message);
        }

        try
        {
            // An antivirus scanning the new file can hold it for a moment, hence the retry.
            await RetryWhileLockedAsync(() =>
            {
                File.Move(tmpPath, targetPath, overwrite: false);
                return Task.FromResult(true);
            }, ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (IOException ex) when (LowWord(ex) is ErrorFileExists or ErrorAlreadyExists)
        {
            DeleteQuietly(tmpPath);
            return Result.Fail(ErrorCode.TargetExists, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(tmpPath);
            return Result.Fail(ErrorCode.IoError, ex.Message);
        }
    }

    public async Task<Result> MoveAsync(string fromPath, string toPath, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(toPath)!);
            await RetryWhileLockedAsync(() =>
            {
                File.Move(fromPath, toPath, overwrite: false);
                return Task.FromResult(true);
            }, ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException)
        {
            return Result.Fail(ErrorCode.FileMissing, fromPath);
        }
        catch (IOException ex) when (LowWord(ex) is ErrorFileExists or ErrorAlreadyExists)
        {
            return Result.Fail(ErrorCode.TargetExists, toPath);
        }
        catch (IOException ex) when (IsLocked(ex))
        {
            return Result.Fail(ErrorCode.FileLocked, fromPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail(ErrorCode.IoError, ex.Message);
        }
    }

    public void TryDeleteEmptyFolders(string folder, string stopAt)
    {
        // "E:\" stays "E:\" (an archive can be a whole disk), "D:\Facturas" becomes "D:\Facturas\".
        var root = Path.GetFullPath(stopAt);
        if (!Path.EndsInDirectorySeparator(root))
            root += Path.DirectorySeparatorChar;
        var current = Path.GetFullPath(folder);
        while (current.StartsWith(root, StringComparison.OrdinalIgnoreCase) && current.Length > root.Length)
        {
            try
            {
                // Without recursive it throws instead of deleting a folder that still has something in it.
                Directory.Delete(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
            current = Path.GetDirectoryName(current)!;
        }
    }

    public async Task<bool> TryDeleteAsync(string path, CancellationToken ct)
    {
        try
        {
            // File.Delete doesn't throw for a missing file, only for a missing folder.
            return await RetryWhileLockedAsync(() =>
            {
                File.Delete(path);
                return Task.FromResult(true);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or DriveNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public Result CreateFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Result.Fail(ErrorCode.IoError, ex.Message);
        }
    }

    public IReadOnlyList<string> ListFiles(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            : [];

    public void TryEmptyFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            DeleteQuietly(file);
        foreach (var subfolder in Directory.GetDirectories(folder))
        {
            try
            {
                Directory.Delete(subfolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public long? AvailableFreeSpace(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            return null;
        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public long GetLength(string path) => new FileInfo(path).Length;

    public DateTime GetLastWriteTime(string path) => File.GetLastWriteTime(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    // Runs the action again while another program has the file open. The last lock error is rethrown.
    private async Task<T> RetryWhileLockedAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (IOException ex) when (IsLocked(ex) && attempt < Attempts)
            {
            }

            await Task.Delay(_retryDelay * attempt, ct).ConfigureAwait(false);
        }
    }

    // Another program has the file open: a sharing or lock violation. Also used by the database backup.
    internal static bool IsLocked(IOException ex) => LowWord(ex) is ErrorSharingViolation or ErrorLockViolation;

    private static int LowWord(IOException ex) => ex.HResult & 0xFFFF;

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
