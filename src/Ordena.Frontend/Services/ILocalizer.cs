using Ordena.Backend.Results;

namespace Ordena.Frontend.Services;

// Texts for ViewModels, in the language chosen at startup. XAML uses {x:Static res:Strings.Key} instead.
public interface ILocalizer
{
    // The language the app is running in: "es" or "en".
    string Language { get; }

    string Get(string key);

    // The text with string.Format's {0}, {1}… filled in.
    string Format(string key, params object?[] args);

    // The message for an expected backend failure ("Error_FileLocked").
    string Error(ErrorCode error);

    // The label for an enum value ("DocType_Factura").
    string Enum<TEnum>(TEnum value) where TEnum : struct, Enum;
}
