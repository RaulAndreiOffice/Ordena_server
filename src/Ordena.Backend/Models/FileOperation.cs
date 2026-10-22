namespace Ordena.Backend.Models;

// Journal row for one file move (import, edit, delete, restore).
public class FileOperation
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    // Paths inside the archive are relative to the archive root, like Documents.RelativePath.
    // An import's FromPath is absolute, because the file comes from outside the archive.
    public string FromPath { get; set; } = string.Empty;
    public string ToPath { get; set; } = string.Empty;

    public FileOperationState State { get; set; }
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }
}
