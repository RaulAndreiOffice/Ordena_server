using Ordena.Backend.Dtos;

namespace Ordena.Backend.Rules;

// Finds how an existing archive names its files: every preset with every separator and date format is
// tried on every name (regex only, a few milliseconds for 3,000 names), and the ones that read the most
// names come first.
internal static class PatternDetector
{
    private static readonly string[] Separators = ["_", "-", " ", "."];

    // {s} is the separator. The order breaks ties: the most common layouts first.
    private static readonly string[] DatedPresets =
    [
        "{fecha}{s}{proveedor}{s}{numero}",
        "{proveedor}{s}{numero}{s}{fecha}",
        "{numero}{s}{proveedor}{s}{fecha}",
    ];

    // When the files sit in one folder per supplier, the name may hold only the date and the number.
    private static readonly string[] FolderPresets =
    [
        "{fecha}{s}{numero}",
        "{numero}{s}{fecha}",
    ];

    // Without a date: the file's own date is offered instead, for the user to check.
    private const string UndatedPreset = "{proveedor} - {numero}";

    public static IReadOnlyList<NamePattern> Rank(IReadOnlyList<string> names, bool partnerFromFolder, int top = 3)
    {
        var templates = partnerFromFolder ? DatedPresets.Concat(FolderPresets) : DatedPresets;
        var candidates = new List<FileNamePattern>();
        foreach (var template in templates)
        {
            foreach (var separator in Separators)
            {
                foreach (var format in FileNamePattern.DateFormats)
                {
                    var created = FileNamePattern.Create(template.Replace("{s}", separator), format, partnerFromFolder);
                    if (created.IsSuccess)
                        candidates.Add(created.Value);
                }
            }
        }
        candidates.Add(FileNamePattern.Create(UndatedPreset, null, partnerFromFolder).Value);

        // OrderBy is stable: among equal counts, a dated pattern first, then the preset order above.
        return candidates
            .Select(p => new NamePattern(p.Pattern, p.DateFormat, names.Count(n => p.Match(n).Status == NameMatch.Ok), names.Count))
            .Where(p => p.Matches > 0)
            .OrderByDescending(p => p.Matches)
            .ThenBy(p => p.DateFormat is null)
            .Take(top)
            .ToList();
    }
}
