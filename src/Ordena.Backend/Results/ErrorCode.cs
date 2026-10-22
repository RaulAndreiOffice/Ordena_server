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
}
