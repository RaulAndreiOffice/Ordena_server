using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Ordena.Backend.Dtos;
using Ordena.Backend.Results;

namespace Ordena.Backend.Rules;

internal enum NameMatch
{
    Ok,

    // The name doesn't have the pattern's shape.
    NotRecognized,

    // The shape fits but the date doesn't exist, like 2026-02-30. Partner and number are still read.
    InvalidDate,
}

// What a file name says. Date is null for a pattern without {fecha}, and for InvalidDate.
internal sealed record ParsedName(NameMatch Status, DateOnly? Date, string? Partner, string? Number);

// A file-name pattern for the bulk import: "{fecha}_{proveedor}_{numero}" with a date format such as
// "dd-MM-yyyy". Anything between the tokens is literal text; a space stands for any run of spaces.
// The partner is greedy and the number lazy, so separators inside a supplier's name stay in the name:
// "2026-03-14_Telefonica_Espana_A-1" is Telefonica Espana, A-1.
internal sealed partial class FileNamePattern
{
    public const string DateToken = "fecha";
    public const string PartnerToken = "proveedor";
    public const string NumberToken = "numero";

    public static IReadOnlyList<string> DateFormats => NamePatterns.DateFormats;

    private readonly Regex _regex;

    // The pattern's one-character separators ("_", "-", "."), which become spaces inside a partner name.
    private readonly string _separators;

    private FileNamePattern(string pattern, string? dateFormat, Regex regex, string separators)
    {
        Pattern = pattern;
        DateFormat = dateFormat;
        _regex = regex;
        _separators = separators;
    }

    public string Pattern { get; }

    // Null when the pattern has no {fecha}.
    public string? DateFormat { get; }

    public bool HasDate => DateFormat is not null;

    // InvalidNamePattern, with the reason as Detail: UnknownToken, RepeatedToken, MissingNumber,
    // MissingPartner (allowed when the partner comes from the folder), TokensTogether (two tokens with
    // nothing between them), or UnknownDateFormat.
    public static Result<FileNamePattern> Create(string pattern, string? dateFormat, bool partnerFromFolder)
    {
        var parts = TokenSplit().Split(pattern);
        var tokens = Token().Matches(pattern).Select(m => m.Groups[1].Value.ToLowerInvariant()).ToList();

        if (tokens.Any(t => t is not (DateToken or PartnerToken or NumberToken)))
            return Fail("UnknownToken");
        if (tokens.Distinct().Count() != tokens.Count)
            return Fail("RepeatedToken");
        if (!tokens.Contains(NumberToken))
            return Fail("MissingNumber");
        if (!tokens.Contains(PartnerToken) && !partnerFromFolder)
            return Fail("MissingPartner");
        // Split leaves the text around each token: an empty piece between two tokens means they touch.
        if (parts.Skip(1).SkipLast(1).Any(p => p.Length == 0))
            return Fail("TokensTogether");

        var hasDate = tokens.Contains(DateToken);
        if (hasDate && (dateFormat is null || !DateFormats.Contains(dateFormat)))
            return Fail("UnknownDateFormat");

        var regex = new StringBuilder("^");
        for (var i = 0; i < parts.Length; i++)
        {
            regex.Append(Literal(parts[i]));
            if (i < tokens.Count)
            {
                regex.Append(tokens[i] switch
                {
                    DateToken => $"(?<{DateToken}>{DateRegex(dateFormat!)})",
                    PartnerToken => $"(?<{PartnerToken}>.+)",
                    _ => $"(?<{NumberToken}>.+?)",
                });
            }
        }
        regex.Append('$');

        var separators = string.Concat(parts.Where(p => p.Length == 1).Distinct());
        return Result.Ok(new FileNamePattern(
            pattern, hasDate ? dateFormat : null,
            new Regex(regex.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            separators));

        static Result<FileNamePattern> Fail(string reason) => Result.Fail<FileNamePattern>(ErrorCode.InvalidNamePattern, reason);
    }

    // nameWithoutExtension: "2026-03-14_Telefonica_Espana_A-1", not the full path.
    public ParsedName Match(string nameWithoutExtension)
    {
        var match = _regex.Match(nameWithoutExtension.Trim());
        if (!match.Success)
            return new ParsedName(NameMatch.NotRecognized, null, null, null);

        var partner = match.Groups[PartnerToken].Success ? CleanPartner(match.Groups[PartnerToken].Value, _separators) : null;
        var number = match.Groups[NumberToken].Value.Trim();
        if (number.Length == 0 || partner?.Length == 0)
            return new ParsedName(NameMatch.NotRecognized, null, null, null);

        if (DateFormat is null)
            return new ParsedName(NameMatch.Ok, null, partner, number);

        return DateOnly.TryParseExact(match.Groups[DateToken].Value, DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? new ParsedName(NameMatch.Ok, date, partner, number)
            : new ParsedName(NameMatch.InvalidDate, null, partner, number);
    }

    // "Telefonica_Espana" → "Telefonica Espana": underscores, and the pattern's own separators, become
    // spaces; leading or trailing dashes go. Other characters stay ("Repsol S.A." with "_" as separator).
    public static string CleanPartner(string text, string separators = "_")
    {
        foreach (var separator in separators.Append('_'))
            text = text.Replace(separator, ' ');
        return Spaces().Replace(text, " ").Trim(' ', '-');
    }

    // Literal text between tokens. A space matches one or more spaces, so "Repsol  -  12" still fits.
    private static string Literal(string text) => Regex.Escape(text).Replace(@"\ ", @"\s+");

    // "dd-MM-yyyy" → \d{2}-\d{2}-\d{4}: the right width, checked as a real date afterwards.
    private static string DateRegex(string format) =>
        Regex.Escape(format).Replace("yyyy", @"\d{4}").Replace("MM", @"\d{2}").Replace("dd", @"\d{2}");

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Token();

    // The same without the group: Regex.Split would otherwise put the token names among the parts.
    [GeneratedRegex(@"\{\w+\}")]
    private static partial Regex TokenSplit();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
