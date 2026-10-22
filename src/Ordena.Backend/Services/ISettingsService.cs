using Ordena.Backend.Dtos;

namespace Ordena.Backend.Services;

public interface ISettingsService
{
    // Language is "es" until the user picks one; ArchiveRoot is null until first run sets it.
    Task<UserSettings> GetAsync(CancellationToken ct);

    // Throws ArgumentException for an empty or relative path.
    Task SetArchiveRootAsync(string path, CancellationToken ct);

    // "es" or "en"; anything else throws ArgumentException.
    Task SetLanguageAsync(string language, CancellationToken ct);
}
