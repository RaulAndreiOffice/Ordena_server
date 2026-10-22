using Ordena.Backend.Files;
using Ordena.Backend.Results;

namespace Ordena.Tests.TestSupport;

// The real storage, except that it can simulate the app dying in the middle of a file operation:
// the disk is left exactly as a crash would leave it, then the call throws instead of returning.
// It can also pretend the disk is full.
internal sealed class CrashingStorage(IFileStorage inner) : IFileStorage
{
    public enum Crash { None, AfterMove, AfterWrite, BeforeRename }

    public Crash When { get; set; }

    // Reported instead of the real free space when set.
    public long? FreeSpace { get; set; }

    public async Task<Result> MoveAsync(string fromPath, string toPath, CancellationToken ct)
    {
        var result = await inner.MoveAsync(fromPath, toPath, ct);
        return When == Crash.AfterMove ? throw new SimulatedCrashException() : result;
    }

    public async Task<Result> WriteVerifiedAsync(
        string targetPath, ReadOnlyMemory<byte> content, string sha256, CancellationToken ct)
    {
        if (When == Crash.BeforeRename)
        {
            // Died while the copy was still "<target>.tmp".
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllBytesAsync(targetPath + ".tmp", content.ToArray(), ct);
            throw new SimulatedCrashException();
        }

        var result = await inner.WriteVerifiedAsync(targetPath, content, sha256, ct);
        return When == Crash.AfterWrite ? throw new SimulatedCrashException() : result;
    }

    public Task<Result<byte[]>> ReadAsync(string path, CancellationToken ct) => inner.ReadAsync(path, ct);
    public Task<bool> TryDeleteAsync(string path, CancellationToken ct) => inner.TryDeleteAsync(path, ct);
    public void TryDeleteEmptyFolders(string folder, string stopAt) => inner.TryDeleteEmptyFolders(folder, stopAt);
    public IReadOnlyList<string> ListFiles(string folder) => inner.ListFiles(folder);
    public void TryEmptyFolder(string folder) => inner.TryEmptyFolder(folder);
    public long? AvailableFreeSpace(string path) => FreeSpace ?? inner.AvailableFreeSpace(path);
    public long GetLength(string path) => inner.GetLength(path);
    public bool FileExists(string path) => inner.FileExists(path);
    public bool DirectoryExists(string path) => inner.DirectoryExists(path);
}

internal sealed class SimulatedCrashException() : Exception("Simulated crash.");
