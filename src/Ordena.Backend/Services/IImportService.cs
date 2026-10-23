using Ordena.Backend.Dtos;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

public interface IImportService
{
    // Moves the PDF into <archive>/<Año>/<nT>/<Recibidas|Emitidas>/<Partner>/ under its standard name
    // and adds its row. Expected failures, where nothing is moved and the original stays in place:
    // - FileMissing, FileLocked, IoError: the file couldn't be read or written
    // - Empty, NotPdf
    // - ArchiveUnavailable: the archive folder doesn't exist
    // - AlreadyArchived, AlreadyInTrash: the same file is already archived; Detail is that document's id
    // - InvalidPartnerName: nothing is left of the name after normalization, for example "S.L."
    // - DuplicateNumber: the partner already has this number; call again with ConfirmDuplicate to import anyway
    // A blank partner or number, or a relative path, throws ArgumentException.
    // With KeepSource the original is copied, not moved.
    Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct);

    // What the import screen asks as soon as a file comes up, before anything is typed:
    // FileMissing, FileLocked, IoError, Empty, NotPdf, or AlreadyArchived / AlreadyInTrash
    // (Detail is that document's id). Ok means the file can be imported. Nothing is changed.
    Task<Result> CheckAsync(string sourcePath, CancellationToken ct);
}
