namespace Ordena.Backend.Dtos;

// What "Comprobar carpeta" found and did.
public sealed record ArchiveCheckReport(
    // Every document checked, deleted ones (in _Papelera) included.
    int DocumentCount,

    // Files that had been moved or renamed by hand, found by name or content and put back.
    IReadOnlyList<RelinkedFile> Relinked,

    // Documents whose file is nowhere in the archive. "Buscar archivo" can still re-link each one.
    IReadOnlyList<DocumentDetails> Missing,

    // Full paths of PDFs in the archive that no document points to. They can go to the import queue.
    IReadOnlyList<string> UnknownFiles);

// FoundAt and MovedTo are relative to the archive root.
public sealed record RelinkedFile(int DocumentId, string FoundAt, string MovedTo);
