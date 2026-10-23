using Ordena.Backend.Dtos;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

public interface ISettingsService
{
    // Language is "es" until the user picks one; ArchiveRoot is null until first run sets it.
    Task<UserSettings> GetAsync(CancellationToken ct);

    // First run's "Empezar de cero", with no documents yet: creates the folder if needed and uses it.
    // IoError when the folder can't be created (a missing drive). Changing the folder later goes through
    // IArchiveService, which checks the files are there. Throws ArgumentException for an empty or relative path.
    Task<Result> SetArchiveRootAsync(string path, CancellationToken ct);

    // What first run proposes: "Facturas" in the user's Documents folder.
    string SuggestArchiveRoot();

    // "es" or "en"; anything else throws ArgumentException.
    Task SetLanguageAsync(string language, CancellationToken ct);
}
