namespace Ordena.Backend.Dtos;

// SourceDeleted is false when the file is archived but the original couldn't be deleted (another program
// had it open; the UI only warns), and in copy mode (KeepSource), where it is kept on purpose.
public sealed record ImportResult(DocumentListItem Document, bool SourceDeleted);
