namespace Ordena.Backend.Models;

// Journal row for one file move (import, edit, delete, restore).
public class FileOperation
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string FromPath { get; set; } = string.Empty;
    public string ToPath { get; set; } = string.Empty;

    public FileOperationState State { get; set; }
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }
}
