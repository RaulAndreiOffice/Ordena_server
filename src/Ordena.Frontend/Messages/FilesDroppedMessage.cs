namespace Ordena.Frontend.Messages;

// PDFs dropped on the main window or picked with "Añadir archivos": the Import page queues them.
// KeepSource: copy them instead of moving them (the files a bulk import couldn't read, from an old
// archive that must stay as it is). StartsPaid: their form starts ticked as paid (that same old
// archive, when the bulk import marked its documents paid); new files start pending.
public sealed record FilesDroppedMessage(IReadOnlyList<string> Paths, bool KeepSource = false, bool StartsPaid = false);
