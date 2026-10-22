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

    // Retries while the file is locked. A file that is already gone counts as deleted.
    // False when it can't be deleted; the caller only warns.
    Task<bool> TryDeleteAsync(string path, CancellationToken ct);

    bool FileExists(string path);
    bool DirectoryExists(string path);
}
