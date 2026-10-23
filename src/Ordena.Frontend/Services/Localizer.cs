using System.Globalization;
using Ordena.Backend.Results;
using Ordena.Frontend.Resources;

namespace Ordena.Frontend.Services;

internal sealed class Localizer : ILocalizer
{
    // The app's language, set at startup in Strings.Culture (see App.ApplyCulture);
    // the current culture when it isn't set, as in tests.
    private static CultureInfo Culture => Strings.Culture ?? CultureInfo.CurrentUICulture;

    public string Language => Culture.TwoLetterISOLanguageName;

    // A missing key shows as the key itself, so it is visible instead of an empty label.
    public string Get(string key) => Strings.ResourceManager.GetString(key, Culture) ?? key;

    public string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    public string Error(ErrorCode error) => Get($"Error_{error}");

    public string Enum<TEnum>(TEnum value) where TEnum : struct, Enum => Get($"{typeof(TEnum).Name}_{value}");
}
