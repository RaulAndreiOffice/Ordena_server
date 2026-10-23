using Ordena.Backend.Dtos;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// A language Ordena can be shown in, for the first-run and Ajustes dropdowns.
public sealed record LanguageOption(string Code, string Name)
{
    // Each language is named in itself, so it can be found whatever the current one is.
    public static IReadOnlyList<LanguageOption> All(ILocalizer localizer) =>
    [
        new(UserSettings.Spanish, localizer.Get("Language_Spanish")),
        new(UserSettings.English, localizer.Get("Language_English")),
    ];

    // What screen readers announce for a dropdown entry, instead of the record's members.
    public override string ToString() => Name;
}
