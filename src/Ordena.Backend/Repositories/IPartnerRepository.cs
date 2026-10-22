using Ordena.Backend.Models;

namespace Ordena.Backend.Repositories;

internal interface IPartnerRepository
{
    // Read-only. Names containing nameNorm, the ones starting with it first.
    Task<IReadOnlyList<Partner>> SuggestAsync(string nameNorm, int max, CancellationToken ct);

    // These two also see partners added in this unit of work but not saved yet,
    // so a batch can't create the same partner or folder twice.
    Task<Partner?> GetByNameNormAsync(string nameNorm, CancellationToken ct);
    Task<bool> FolderNameExistsAsync(string folderName, CancellationToken ct);

    Task<bool> AnyAsync(CancellationToken ct);

    void Add(Partner partner);

    // Only to undo an import that created the partner and then failed.
    void Remove(Partner partner);
}
