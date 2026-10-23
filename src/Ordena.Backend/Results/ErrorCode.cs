namespace Ordena.Backend.Results;

// Expected failures a service can return. The Frontend maps each code to a localized message.
public enum ErrorCode
{
    NotPdf,
    Empty,
    AlreadyArchived,
    AlreadyInTrash,
    DuplicateNumber,
    FileLocked,
    FileMissing,
    TargetExists,
    IoError,
    InvalidPartnerName,

    // The archive folder doesn't exist, for example on an unplugged USB disk.
    ArchiveUnavailable,

    // "Buscar archivo" picked a file whose content isn't the document's (another SHA-256).
    NotSameFile,

    // Moving the archive: the target disk lacks room for it plus 10%. Detail is the bytes needed.
    NotEnoughSpace,

    // "Ya tengo un archivo": no backup in <archive>/_backup that opens and passes SQLite's check.
    NoBackup,

    // "Cambiar ubicación": the chosen folder doesn't hold this archive's files. Detail is "found X of Y".
    ArchiveMismatch,

    // The target is the archive itself, a folder inside it, or a folder that contains it.
    InvalidArchiveFolder,

    // Bulk import: a file-name pattern that can't be used. Detail is the reason (see FileNamePattern.Create).
    InvalidNamePattern,
}
