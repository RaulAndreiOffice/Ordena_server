using Ordena.Backend.Dtos;
using Ordena.Backend.Models;

namespace Ordena.Backend.Repositories;

internal interface IDocumentRepository
{
    // Tracked, with its Partner, for edits.
    Task<Document?> GetByIdAsync(int id, CancellationToken ct);

    // Deleted documents included: a file already in _Papelera must not be imported again.
    Task<Document?> GetByHashAsync(string sha256, CancellationToken ct);

    // Deleted documents excluded. exceptId skips the document being edited.
    Task<bool> NumberExistsAsync(int partnerId, string docNumberNorm, int? exceptId, CancellationToken ct);

    // Deleted documents included, like the unique index. Case is ignored (NOCASE).
    Task<bool> RelativePathExistsAsync(string relativePath, CancellationToken ct);

    // Every document with its Partner, deleted ones included (their files are in _Papelera), read-only.
    // For "Comprobar carpeta".
    Task<IReadOnlyList<Document>> ListAllAsync(CancellationToken ct);

    // The latest UpdatedAt, or null without documents. Every import, edit, delete and restore sets it,
    // so it says whether anything changed since the last backup.
    Task<DateTime?> GetLastUpdateAsync(CancellationToken ct);

    // Only deleted, read-only, the most recently deleted first. For the trash.
    Task<IReadOnlyList<DocumentDetails>> ListDeletedAsync(CancellationToken ct);

    // Each active document's partner and number, normalized: the bulk import marks numbers that exist.
    Task<IReadOnlyList<(string PartnerNameNorm, string DocNumberNorm)>> GetNumberKeysAsync(CancellationToken ct);

    // The years that have documents, not deleted ones, the newest first. For the year filter.
    Task<IReadOnlyList<int>> GetYearsAsync(CancellationToken ct);

    // Not deleted, read-only, only the grid columns.
    Task<IReadOnlyList<DocumentListItem>> SearchAsync(DocumentSearch search, int limit, CancellationToken ct);
    Task<int> CountAsync(DocumentSearch search, CancellationToken ct);

    void Add(Document document);

    // Only to undo an import whose file couldn't be written. Deleting a document is a soft delete.
    void Remove(Document document);
}
