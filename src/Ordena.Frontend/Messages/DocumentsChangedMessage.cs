namespace Ordena.Frontend.Messages;

public enum DocumentsChange
{
    Imported,

    // A whole folder ("Importar carpeta"); DocumentId is 0.
    BulkImported,
    Edited,
    Deleted,
    Restored,
}

// Sent after a document is added, edited, deleted or restored, so pages that list documents
// (Search, Trash) reload.
public sealed record DocumentsChangedMessage(DocumentsChange Kind, int DocumentId);
