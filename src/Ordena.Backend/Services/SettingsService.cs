using System.Globalization;
using Ordena.Backend.Dtos;
using Ordena.Backend.Repositories;

namespace Ordena.Backend.Services;

internal sealed class SettingsService(IUnitOfWorkFactory uowFactory) : ISettingsService
{
    public Task<UserSettings> GetAsync(CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        var values = await uow.Settings.GetAllAsync(ct).ConfigureAwait(false);

        DateTime? lastBackup = values.TryGetValue(SettingKeys.LastBackup, out var text)
            ? DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;

        return new UserSettings(
            values.GetValueOrDefault(SettingKeys.ArchiveRoot),
            values.GetValueOrDefault(SettingKeys.Language) ?? UserSettings.Spanish,
            lastBackup);
    }, ct);

    public Task SetArchiveRootAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException($"The archive root must be an absolute path: '{path}'.", nameof(path));

        return SetAsync(SettingKeys.ArchiveRoot, path, ct);
    }

    public Task SetLanguageAsync(string language, CancellationToken ct)
    {
        if (language is not (UserSettings.Spanish or UserSettings.English))
            throw new ArgumentException($"Unsupported language: '{language}'.", nameof(language));

        return SetAsync(SettingKeys.Language, language, ct);
    }

    private Task SetAsync(string key, string value, CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        await uow.Settings.SetAsync(key, value, ct).ConfigureAwait(false);
        await uow.SaveChangesAsync(ct).ConfigureAwait(false);
    }, ct);
}
