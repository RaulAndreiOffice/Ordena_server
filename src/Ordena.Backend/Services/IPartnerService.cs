using Ordena.Backend.Models;

namespace Ordena.Backend.Services;

public interface IPartnerService
{
    // Autocomplete: partners whose name contains the typed text (accents and case ignored),
    // the ones starting with it first. Empty text gives an empty list.
    Task<IReadOnlyList<Partner>> SuggestAsync(string text, int max, CancellationToken ct);

    // The partner with the same name after normalization, for "¿Quería decir…?":
    // "Telefonica Espana SA" finds "Telefónica España, S.A.U.".
    Task<Partner?> FindSimilarAsync(string name, CancellationToken ct);
}
