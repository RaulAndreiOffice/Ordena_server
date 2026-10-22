using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ordena.Backend.Rules;

// The same normalization is used when saving and when searching, so both sides always match.
internal static partial class TextNormalizer
{
    // "á" → "a", "ñ" → "n", "ç" → "c", "ü" → "u".
    public static string RemoveDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    // Removes a trailing S.L., S.L.U., S.A., S.A.U., S.Coop. or C.B. (dots and spaces optional).
    // It must follow a space or a comma, so a name like "Casa" is left alone.
    public static string StripLegalForm(string name) => LegalForm().Replace(name, "");

    // Lowercase, no accents, letters and digits only, words joined by one space.
    public static string NormalizeWords(string text) =>
        string.Join(' ', Word().Matches(RemoveDiacritics(text).ToLowerInvariant()).Select(m => m.Value));

    // "Telefónica España, S.A.U." → "telefonica espana". Used for search and the similar-name check.
    public static string NormalizeName(string name) => NormalizeWords(StripLegalForm(name));

    // "A-2026/0123" → "A20260123". Typing "0123" or "2026/0123" then both match.
    public static string NormalizeNumber(string number)
    {
        var text = NumberPrefix().Replace(RemoveDiacritics(number).ToUpperInvariant(), "");
        return NotAsciiLetterOrDigit().Replace(text, "");
    }

    [GeneratedRegex(@"(?:^|[\s,]+)(?:S\.?\s*L\.?\s*U|S\.?\s*L|S\.?\s*A\.?\s*U|S\.?\s*A|S\.?\s*Coop|C\.?\s*B)\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LegalForm();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    // "Nº 123", "Núm. 123", "Número 123", "No. 123" (already uppercase and without accents).
    [GeneratedRegex(@"^\s*(?:N\s*[º°ª]\.?|NUMERO|NUM\.?|NO\.)\s*")]
    private static partial Regex NumberPrefix();

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NotAsciiLetterOrDigit();
}
