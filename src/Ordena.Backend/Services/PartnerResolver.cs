using Ordena.Backend.Infrastructure;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

// Finds the partner for a typed name, or adds a new one to the caller's unit of work.
// Shared by import and bulk import. It never saves, so the partner commits together with its document.
internal sealed class PartnerResolver(IClock clock)
{
    public async Task<Result<Partner>> GetOrCreateAsync(IUnitOfWork uow, string name, CancellationToken ct)
    {
        var nameNorm = TextNormalizer.NormalizeName(name);
        var folderName = PathSanitizer.ToFolderName(name);
        if (nameNorm.Length == 0 || folderName.Length == 0)
            return Result.Fail<Partner>(ErrorCode.InvalidPartnerName, name);

        var existing = await uow.Partners.GetByNameNormAsync(nameNorm, ct).ConfigureAwait(false);
        if (existing is not null)
            return Result.Ok(existing);

        var partner = new Partner
        {
            Name = name.Trim(),
            NameNorm = nameNorm,
            FolderName = await UniqueFolderNameAsync(uow, folderName, ct).ConfigureAwait(false),
            CreatedAt = clock.UtcNow,
        };
        uow.Partners.Add(partner);
        return Result.Ok(partner);
    }

    // "Repsol" taken → "Repsol-2", "Repsol-3"…, cut so the suffix still fits in 40 characters.
    private static async Task<string> UniqueFolderNameAsync(IUnitOfWork uow, string folderName, CancellationToken ct)
    {
        var candidate = folderName;
        for (var n = 2; await uow.Partners.FolderNameExistsAsync(candidate, ct).ConfigureAwait(false); n++)
        {
            var suffix = $"-{n}";
            var maxBase = PathSanitizer.PartnerMaxLength - suffix.Length;
            candidate = folderName[..Math.Min(folderName.Length, maxBase)].TrimEnd('-') + suffix;
        }
        return candidate;
    }
}
