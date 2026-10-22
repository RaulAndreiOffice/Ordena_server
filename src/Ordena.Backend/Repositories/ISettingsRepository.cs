namespace Ordena.Backend.Repositories;

internal interface ISettingsRepository
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct);

    // Inserts the key, or updates it when it already exists.
    Task SetAsync(string key, string value, CancellationToken ct);
}
