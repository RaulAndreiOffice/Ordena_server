using Ordena.Backend.Results;

namespace Ordena.Backend.Files;

// All disk access of the services. Expected problems (missing, locked, disk errors) are Results.
internal interface IFileStorage
{
    // The whole file, retrying while another program has it locked.
    // FileMissing, FileLocked or IoError.
    Task<Result<byte[]>> ReadAsync(string path, CancellationToken ct);

    // Writes "<target>.tmp", checks its hash, then renames it to the target. Never overwrites.
    // TargetExists or IoError. After a failure there is no target file and no tmp file.
    Task<Result> WriteVerifiedAsync(string targetPath, ReadOnlyMemory<byte> content, string sha256, CancellationToken ct);

    // Renames or moves a file inside the archive (same disk, so the move is atomic). Never overwrites.
    // FileMissing, TargetExists, FileLocked or IoError; after a failure the file is where it was.
    Task<Result> MoveAsync(string fromPath, string toPath, CancellationToken ct);

    // Removes the folder and then its parents while they are empty, never stopAt itself.
    void TryDeleteEmptyFolders(string folder, string stopAt);

    // Retries while the file is locked. A file that is already gone counts as deleted.
    // False when it can't be deleted; the caller only warns.
    Task<bool> TryDeleteAsync(string path, CancellationToken ct);

    // Full paths of every file under the folder, in all subfolders. Empty when the folder doesn't exist.
    IReadOnlyList<string> ListFiles(string folder);

    // Deletes what is in the folder, skipping anything still open. Creates the folder when it's missing.
    void TryEmptyFolder(string folder);

    // Free bytes on the disk that holds the path, or null when that can't be told (a network share).
    long? AvailableFreeSpace(string path);

    long GetLength(string path);
    bool FileExists(string path);
    bool DirectoryExists(string path);
}
