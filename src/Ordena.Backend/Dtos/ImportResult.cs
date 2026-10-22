namespace Ordena.Backend.Dtos;

// SourceDeleted is false when the file is archived but the original couldn't be deleted
// (another program had it open). The import still succeeded; the UI only warns.
public sealed record ImportResult(DocumentListItem Document, bool SourceDeleted);
