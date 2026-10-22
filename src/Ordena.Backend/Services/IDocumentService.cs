using Ordena.Backend.Dtos;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

// Edit, delete, restore and open archived documents. The file on disk always follows the row:
// a change that affects the name renames or moves the file, through the journal.
// An unknown id throws KeyNotFoundException, because the screens only show documents that exist.
// Every method that moves a file can fail with ArchiveUnavailable, and with FileMissing,
// FileLocked, TargetExists or IoError when the file couldn't be moved; then nothing changes.
public interface IDocumentService
{
    Task<DocumentDetails> GetAsync(int id, CancellationToken ct);

    // Saves the new values and renames or moves the file to match: another partner, number or type,
    // or a date in another quarter or year. Also InvalidPartnerName, and DuplicateNumber when the
    // partner already has that number (call again with ConfirmDuplicate to save anyway).
    // A blank partner or number throws ArgumentException; a deleted document InvalidOperationException.
    Task<Result<DocumentListItem>> EditAsync(EditDocumentRequest request, CancellationToken ct);

    // Soft delete: the file moves to _Papelera and the document leaves search.
    // An already deleted document throws InvalidOperationException.
    Task<Result> DeleteAsync(int id, CancellationToken ct);

    // Back to its standard name and folder, with _2 if that name was taken meanwhile.
    // A document that isn't deleted throws InvalidOperationException.
    Task<Result> RestoreAsync(int id, CancellationToken ct);

    // The deleted documents, the most recently deleted first.
    Task<IReadOnlyList<DocumentDetails>> ListTrashAsync(CancellationToken ct);

    // The file on disk, to open it or show it in Explorer.
    // FileMissing, with the expected path as Detail, when it isn't there ("Fichero no encontrado").
    Task<Result<string>> GetFullPathAsync(int id, CancellationToken ct);

    // "Buscar archivo" for a missing file: puts the chosen file back where the document expects it.
    // It must be the same file (same SHA-256), otherwise NotSameFile. A file found inside the archive
    // is moved; one from elsewhere (another disk, the desktop) is copied and left where it is.
    // A relative path throws ArgumentException.
    Task<Result> RelinkAsync(int id, string path, CancellationToken ct);
}
