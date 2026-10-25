using Ordena.Backend.Files;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

// The archive folder from Settings, checked before any file in it is touched.
internal static class ArchiveRoot
{
    // ArchiveUnavailable when the folder doesn't exist, for example on an unplugged USB disk.
    // It is never recreated, because it may be on the wrong disk.
    public static async Task<Result<string>> GetAsync(IUnitOfWork uow, IFileStorage storage, CancellationToken ct)
    {
        var settings = await uow.Settings.GetAllAsync(ct).ConfigureAwait(false);
        var root = settings.GetValueOrDefault(SettingKeys.ArchiveRoot)
            ?? throw new InvalidOperationException("The archive root is not set. First run sets it before anything else.");

        return storage.DirectoryExists(root)
            ? Result.Ok(root)
            : Result.Fail<string>(ErrorCode.ArchiveUnavailable, root);
    }

    // The same, in a unit of work of its own.
    public static async Task<Result<string>> GetAsync(IUnitOfWorkFactory uowFactory, IFileStorage storage, CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        return await GetAsync(uow, storage, ct).ConfigureAwait(false);
    }
}
