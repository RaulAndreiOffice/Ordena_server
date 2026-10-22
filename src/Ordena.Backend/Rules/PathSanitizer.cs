using System.Text.RegularExpressions;

namespace Ordena.Backend.Rules;

internal static partial class PathSanitizer
{
    public const int PartnerMaxLength = 40;
    public const int NumberMaxLength = 30;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // "Telefónica España, S.A.U." → "Telefonica-Espana". Fixed when the partner is created.
    public static string ToFolderName(string partnerName) =>
        Sanitize(TextNormalizer.StripLegalForm(partnerName), PartnerMaxLength);

    // A safe Windows name part: ASCII letters and digits, words joined by "-".
    // Everything else is a separator, which removes / \ < > : " | ? *, control characters,
    // dots and spaces (including trailing ones) in a single rule.
    public static string Sanitize(string text, int maxLength)
    {
        var words = AsciiWord().Matches(TextNormalizer.RemoveDiacritics(text)).Select(m => m.Value);
        var result = string.Join('-', words);

        if (result.Length > maxLength)
            result = result[..maxLength].TrimEnd('-');

        return ReservedNames.Contains(result) ? result + "_" : result;
    }

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex AsciiWord();
}
