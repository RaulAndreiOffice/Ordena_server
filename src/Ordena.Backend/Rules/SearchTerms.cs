using System.Text.RegularExpressions;

namespace Ordena.Backend.Rules;

// One word of the search box, normalized like the stored columns:
// Name is compared with Partners.NameNorm, Number with Documents.DocNumberNorm.
internal sealed record SearchTerm(string Name, string Number, int? Year);

internal static partial class SearchTerms
{
    // Splits on whitespace. A word with nothing left after normalization (such as "%" or "_")
    // is dropped, so user input can never act as a LIKE wildcard.
    public static IReadOnlyList<SearchTerm> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var terms = new List<SearchTerm>();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = TextNormalizer.NormalizeWords(word);
            var number = TextNormalizer.NormalizeNumber(word);
            int? year = Year().IsMatch(word) ? int.Parse(word) : null;

            if (name.Length > 0 || number.Length > 0)
                terms.Add(new SearchTerm(name, number, year));
        }
        return terms;
    }

    [GeneratedRegex(@"^20\d\d$")]
    private static partial Regex Year();
}
