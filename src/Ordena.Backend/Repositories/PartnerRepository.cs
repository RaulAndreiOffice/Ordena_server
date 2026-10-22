using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Data;
using Ordena.Backend.Models;

namespace Ordena.Backend.Repositories;

internal sealed class PartnerRepository(AppDbContext db) : IPartnerRepository
{
    public async Task<IReadOnlyList<Partner>> SuggestAsync(string nameNorm, int max, CancellationToken ct)
    {
        var contains = $"%{nameNorm}%";
        var startsWith = $"{nameNorm}%";

        return await db.Partners.AsNoTracking()
            .Where(p => EF.Functions.Like(p.NameNorm, contains))
            .OrderBy(p => EF.Functions.Like(p.NameNorm, startsWith) ? 0 : 1)
            .ThenBy(p => p.NameNorm)
            .Take(max)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<Partner?> GetByNameNormAsync(string nameNorm, CancellationToken ct) =>
        db.Partners.Local.FirstOrDefault(p => p.NameNorm == nameNorm)
        ?? await db.Partners.FirstOrDefaultAsync(p => p.NameNorm == nameNorm, ct).ConfigureAwait(false);

    // The column is NOCASE, so the database check ignores case; the local check must too.
    public async Task<bool> FolderNameExistsAsync(string folderName, CancellationToken ct) =>
        db.Partners.Local.Any(p => string.Equals(p.FolderName, folderName, StringComparison.OrdinalIgnoreCase))
        || await db.Partners.AnyAsync(p => p.FolderName == folderName, ct).ConfigureAwait(false);

    public Task<bool> AnyAsync(CancellationToken ct) => db.Partners.AnyAsync(ct);

    public void Add(Partner partner) => db.Partners.Add(partner);

    public void Remove(Partner partner) => db.Partners.Remove(partner);
}
