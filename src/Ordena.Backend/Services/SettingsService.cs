using System.Globalization;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

internal sealed class SettingsService(IUnitOfWorkFactory uowFactory, IFileStorage storage) : ISettingsService
{
    // "Documentos\Facturas" on Spanish Windows; Documents may be redirected to OneDrive.
    public string SuggestArchiveRoot()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(documents, "Facturas");
    }

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

    public Task<Result> SetArchiveRootAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException($"The archive root must be an absolute path: '{path}'.", nameof(path));

        return Task.Run(async () =>
        {
            var created = storage.CreateFolder(path);
            if (!created.IsSuccess)
                return created;

            await SetAsync(SettingKeys.ArchiveRoot, path, ct).ConfigureAwait(false);
            return Result.Ok();
        }, ct);
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
