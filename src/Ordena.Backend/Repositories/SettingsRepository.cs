using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Data;
using Ordena.Backend.Models;

namespace Ordena.Backend.Repositories;

internal sealed class SettingsRepository(AppDbContext db) : ISettingsRepository
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct).ConfigureAwait(false);

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        var setting = await db.Settings.FindAsync([key], ct).ConfigureAwait(false);
        if (setting is null)
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        else
            setting.Value = value;
    }
}
