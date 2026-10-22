using System.Globalization;
using Ordena.Backend.Models;

namespace Ordena.Backend.Rules;

// Where a document lives in the archive. Folder names and suffixes are fixed Spanish names,
// never localized, so the gestoría can read the archive outside the app.
internal static class ArchiveNaming
{
    // Soft-deleted documents, with their folders kept: "_Papelera/2026/1T/Recibidas/…/file.pdf".
    public const string TrashFolder = "_Papelera";

    public static string BuildRelativePath(Document document, Partner partner) =>
        BuildRelativePath(document.IssueDate, document.Direction, document.Type, document.DocNumber, partner.FolderName);

    // "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf".
    // Always "/" as the separator, so a path has one spelling in the database.
    public static string BuildRelativePath(
        DateOnly issueDate, Direction direction, DocType type, string docNumber, string partnerFolder)
    {
        var folder = string.Create(CultureInfo.InvariantCulture,
            $"{issueDate.Year}/{Quarter.Of(issueDate)}T/{DirectionFolder(direction)}/{partnerFolder}");

        var number = PathSanitizer.Sanitize(docNumber, PathSanitizer.NumberMaxLength);
        var numberPart = number.Length > 0 ? "_" + number : "";
        var fileName = string.Create(CultureInfo.InvariantCulture,
            $"{issueDate:yyyy-MM-dd}_{partnerFolder}{numberPart}{TypeSuffix(type)}.pdf");

        return $"{folder}/{fileName}";
    }

    public static string ToTrash(string relativePath) => $"{TrashFolder}/{relativePath}";

    public static string DirectionFolder(Direction direction) => direction switch
    {
        Direction.Recibida => "Recibidas",
        Direction.Emitida => "Emitidas",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    // Shows what a file is from its name alone. A Factura, the usual case, has no suffix.
    public static string TypeSuffix(DocType type) => type switch
    {
        DocType.Factura => "",
        DocType.FacturaSimplificada => "_Ticket",
        DocType.FacturaRectificativa => "_Rectif",
        DocType.Recibo => "_Recibo",
        DocType.Proforma => "_Proforma",
        DocType.Otro => "_Otro",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    // For a name that is already taken: "…_A-2026-0123.pdf" → "…_A-2026-0123_2.pdf".
    public static string WithCopyNumber(string relativePath, int number)
    {
        var extension = Path.GetExtension(relativePath);
        return string.Create(CultureInfo.InvariantCulture,
            $"{relativePath[..^extension.Length]}_{number}{extension}");
    }

    // The path on disk, with "/" turned into "\".
    public static string ToFullPath(string archiveRoot, string relativePath) =>
        Path.GetFullPath(Path.Combine(archiveRoot, relativePath));

    // The other way round, with "/": "D:\Facturas\2026\1T\a.pdf" → "2026/1T/a.pdf".
    public static string ToRelativePath(string archiveRoot, string fullPath) =>
        Path.GetRelativePath(archiveRoot, fullPath).Replace('\\', '/');

    // True for a file somewhere under the archive root, false for one outside it.
    public static bool IsInArchive(string archiveRoot, string fullPath) =>
        Path.GetFullPath(fullPath).StartsWith(WithEndingSeparator(archiveRoot), StringComparison.OrdinalIgnoreCase);

    // True when the two folders are the same, or one is inside the other: an archive can't move there.
    public static bool AreNested(string folder, string otherFolder)
    {
        var a = WithEndingSeparator(folder);
        var b = WithEndingSeparator(otherFolder);
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    // The inverse of BuildRelativePath, WithCopyNumber and ToTrash, for rebuilding the index from the
    // archive: "[_Papelera/]2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812_Ticket[_2].pdf".
    // Null for anything else, including folders that don't match the date or the partner in the name.
    // The number comes back as it is in the name (sanitized: "A-2026-0123" for "A-2026/0123").
    public static ArchivePath? TryParse(string relativePath)
    {
        var parts = relativePath.Split('/');
        var inTrash = parts[0].Equals(TrashFolder, StringComparison.OrdinalIgnoreCase);
        if (inTrash)
            parts = parts[1..];
        if (parts.Length != 5 || !parts[4].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return null;

        var (yearFolder, quarterFolder, directionFolder, partnerFolder) = (parts[0], parts[1], parts[2], parts[3]);
        var name = parts[4][..^".pdf".Length];

        // "{yyyy-MM-dd}_{partner folder}" first; the partner folder may itself end in "_" (a reserved name).
        if (name.Length < 11 || name[10] != '_'
            || !DateOnly.TryParseExact(name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !name[11..].StartsWith(partnerFolder, StringComparison.OrdinalIgnoreCase))
            return null;

        // Then "_{number}", "_{type suffix}" and "_{copy number}", each optional. A sanitized number never
        // contains "_", so the parts split cleanly; a lone all-digit part is the number, not a copy number.
        var rest = name[(11 + partnerFolder.Length)..];
        if (rest.Length > 0 && rest[0] != '_')
            return null;
        var segments = rest.Length == 0 ? [] : rest[1..].Split('_').ToList();
        if (segments.Any(s => s.Length == 0))
            return null;

        if (segments.Count >= 2 && segments[^1].All(char.IsAsciiDigit))
            segments.RemoveAt(segments.Count - 1);
        var type = DocType.Factura;
        if (segments.Count > 0 && TypesBySuffix.TryGetValue("_" + segments[^1], out var suffixType))
        {
            type = suffixType;
            segments.RemoveAt(segments.Count - 1);
        }
        if (segments.Count > 1)
            return null;

        Direction? direction = directionFolder.ToUpperInvariant() switch
        {
            "RECIBIDAS" => Direction.Recibida,
            "EMITIDAS" => Direction.Emitida,
            _ => null,
        };
        var consistent = direction is not null
            && yearFolder == date.Year.ToString(CultureInfo.InvariantCulture)
            && quarterFolder.Equals($"{Quarter.Of(date)}T", StringComparison.OrdinalIgnoreCase);

        return consistent
            ? new ArchivePath(date, direction!.Value, type, partnerFolder, segments.Count == 1 ? segments[0] : "", inTrash)
            : null;
    }

    private static readonly Dictionary<string, DocType> TypesBySuffix = Enum.GetValues<DocType>()
        .Where(t => TypeSuffix(t).Length > 0)
        .ToDictionary(TypeSuffix, StringComparer.Ordinal);

    // "E:\" stays "E:\", "D:\Facturas" becomes "D:\Facturas\".
    private static string WithEndingSeparator(string folder)
    {
        var full = Path.GetFullPath(folder);
        return Path.EndsInDirectorySeparator(full) ? full : full + Path.DirectorySeparatorChar;
    }
}

// What a path in the archive says about its document. DocNumber is the sanitized number from the name.
internal sealed record ArchivePath(
    DateOnly IssueDate, Direction Direction, DocType Type, string PartnerFolder, string DocNumber, bool InTrash);
