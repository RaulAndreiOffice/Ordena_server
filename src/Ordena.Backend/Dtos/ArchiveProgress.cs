namespace Ordena.Backend.Dtos;

// Reported after each file while the archive is moved or the index rebuilt: "Archivo 120 de 3.000".
public sealed record ArchiveProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal);
