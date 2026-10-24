using Ordena.Backend.Models;
using Ordena.Backend.Results;

namespace Ordena.Backend.Dtos;

// What a typed pattern may use: the tokens, and the date formats the presets are tried with.
public static class NamePatterns
{
    public static readonly IReadOnlyList<string> DateFormats = ["yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "yyyyMMdd", "ddMMyyyy"];
}

// A file-name pattern ("{fecha}_{proveedor}_{numero}", "dd-MM-yyyy") and how many of the folder's names
// it reads: "2.871 de 3.000 coinciden". DateFormat is null for a pattern without {fecha}.
public sealed record NamePattern(string Pattern, string? DateFormat, int Matches, int Total);

// Step 1 of "Importar carpeta": the folder of an existing archive, and what all its files are.
public sealed record BulkImportOptions
{
    public required string Folder { get; init; }
    public required Direction Direction { get; init; }
    public DocType Type { get; init; } = DocType.Factura;

    // The partner is the name of each file's folder (one folder per supplier), not a part of the name.
    public bool PartnerFromFolder { get; init; }

    // A pattern typed by the user, with its date format. Null: the one detected that reads the most names.
    public string? Pattern { get; init; }
    public string? DateFormat { get; init; }
}

public enum BulkRowStatus
{
    Ok,
    NotRecognized,

    // The name has a date that doesn't exist, like 2026-02-30.
    InvalidDate,

    // The pattern has no date: IssueDate is the file's own date, to be checked.
    CheckDate,

    // The partner already has this number, in the archive or earlier in this folder.
    NumberExists,
}

// One PDF of the folder as its name reads. Partner, number and date are null when not recognized.
public sealed record BulkImportRow(
    string Path,
    string RelativePath,
    BulkRowStatus Status,
    DateOnly? IssueDate,
    string? PartnerName,
    string? DocNumber);

// A partner name found in the folder, with how many files use it. ExistingName is the archive's
// partner with the same name once normalized ("Telefonica" → "Telefónica España, S.A.U." only if equal).
public sealed record BulkPartner(string Name, int Count, string? ExistingName);

// Names that are probably the same partner ("Telefonica" ×120, "Telefonica Espana" ×3): the same once
// normalized, or one's words inside the other's. SuggestedName is the archive's partner when one matches,
// otherwise the spelling used most (the longer one on a tie).
public sealed record BulkPartnerGroup(IReadOnlyList<string> Names, string SuggestedName);

public sealed record BulkImportPreview(
    string Folder,
    NamePattern? Pattern,
    IReadOnlyList<NamePattern> Detected,
    IReadOnlyList<BulkImportRow> Rows,
    IReadOnlyList<BulkPartner> Partners,
    IReadOnlyList<BulkPartnerGroup> Groups);

// One file to import, with its final values (as fixed in the preview).
public sealed record BulkImportItem(
    string Path,
    DateOnly IssueDate,
    string PartnerName,
    string DocNumber,
    DocType Type,
    bool IncludeDuplicateNumber);

// What the wizard imports. PartnerMap renames partners before import (a merged group's names → one name);
// a name not in it is used as it is.
public sealed record BulkImportRun(
    Direction Direction,
    IReadOnlyList<BulkImportItem> Items,
    IReadOnlyDictionary<string, string> PartnerMap,
    bool MarkPaid = true);

// Reported after each file: "1.234 de 3.000 · factura.pdf".
public sealed record BulkImportProgress(int Done, int Total, string CurrentFile);

public sealed record BulkImportFailure(string Path, ErrorCode Error);

// What the run did. Duplicates are files already archived (or in the trash), and NumberExists files
// whose partner already had that number; neither was copied. Cancelled: stopped between two files.
public sealed record BulkImportReport(
    int Imported,
    IReadOnlyList<string> Duplicates,
    IReadOnlyList<string> NumberExists,
    IReadOnlyList<BulkImportFailure> Failed,
    bool Cancelled,
    TimeSpan Elapsed);
