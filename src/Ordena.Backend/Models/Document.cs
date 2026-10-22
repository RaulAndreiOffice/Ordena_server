namespace Ordena.Backend.Models;

public class Document
{
    public int Id { get; set; }

    // Blocks importing the same file twice, deleted rows included.
    public string Sha256 { get; set; } = string.Empty;

    public int PartnerId { get; set; }
    public Partner Partner { get; set; } = null!;

    // As typed: "A-2026/0123"
    public string DocNumber { get; set; } = string.Empty;

    // "A20260123": used for search and the duplicate check.
    public string DocNumberNorm { get; set; } = string.Empty;

    // Gives the year and quarter folders and the file-name prefix.
    public DateOnly IssueDate { get; set; }

    public DocType Type { get; set; }
    public Direction Direction { get; set; }

    // Relative to the archive root, so the archive can be moved.
    public string RelativePath { get; set; } = string.Empty;

    // File name before import.
    public string OriginalName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTime ImportedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Soft delete: the file is in _Papelera and the row is hidden from search.
    public DateTime? DeletedAt { get; set; }
}
