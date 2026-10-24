namespace Ordena.Backend.Dtos;

// "Mover carpeta de facturas": everything was copied and checked, and the app now uses NewRoot.
// OldRoot is still there; IArchiveService.DeleteOldCopyAsync removes it if the user agrees.
public sealed record ArchiveMoveReport(string OldRoot, string NewRoot, int FileCount, long ByteCount);

// "Ya tengo un archivo": the backup that was restored, when it was made (local time, as in its name),
// and how many documents search now shows.
public sealed record RestoreReport(string BackupFile, DateTime BackupTime, int DocumentCount);

// "Reconstruir índice". Unrecognized and Duplicates are full paths of PDFs that were not indexed:
// names that don't follow the app's pattern (or files that couldn't be read), and second copies of
// a file already indexed. They can go to the import queue.
public sealed record RebuildReport(
    int DocumentCount,
    int PartnerCount,
    IReadOnlyList<string> Unrecognized,
    IReadOnlyList<string> Duplicates);
