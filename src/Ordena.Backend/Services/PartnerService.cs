using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

internal sealed class PartnerService(IUnitOfWorkFactory uowFactory) : IPartnerService
{
    public Task<IReadOnlyList<Partner>> SuggestAsync(string text, int max, CancellationToken ct)
    {
        var nameNorm = TextNormalizer.NormalizeName(text);
        if (nameNorm.Length == 0)
            return Task.FromResult<IReadOnlyList<Partner>>([]);

        return Task.Run(async () =>
        {
            await using var uow = uowFactory.Create();
            return await uow.Partners.SuggestAsync(nameNorm, max, ct).ConfigureAwait(false);
        }, ct);
    }

    public Task<Partner?> FindSimilarAsync(string name, CancellationToken ct)
    {
        var nameNorm = TextNormalizer.NormalizeName(name);
        if (nameNorm.Length == 0)
            return Task.FromResult<Partner?>(null);

        return Task.Run(async () =>
        {
            await using var uow = uowFactory.Create();
            return await uow.Partners.GetByNameNormAsync(nameNorm, ct).ConfigureAwait(false);
        }, ct);
    }
}
