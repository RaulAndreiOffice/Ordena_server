using System.Globalization;
using Ordena.Backend.Models;

namespace Ordena.Backend.Rules;

// Where a document lives in the archive. Folder names and suffixes are fixed Spanish names,
// never localized, so the gestoría can read the archive outside the app.
internal static class ArchiveNaming
{
    // "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf".
    // Always "/" as the separator, so a path has one spelling in the database.
    public static string BuildRelativePath(Document document, Partner partner)
    {
        var date = document.IssueDate;
        var folder = string.Create(CultureInfo.InvariantCulture,
            $"{date.Year}/{Quarter.Of(date)}T/{DirectionFolder(document.Direction)}/{partner.FolderName}");

        var number = PathSanitizer.Sanitize(document.DocNumber, PathSanitizer.NumberMaxLength);
        var numberPart = number.Length > 0 ? "_" + number : "";
        var fileName = string.Create(CultureInfo.InvariantCulture,
            $"{date:yyyy-MM-dd}_{partner.FolderName}{numberPart}{TypeSuffix(document.Type)}.pdf");

        return $"{folder}/{fileName}";
    }

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
}
