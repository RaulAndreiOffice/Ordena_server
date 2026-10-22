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

    // Not deleted, read-only. For the missing-file check.
    Task<IReadOnlyList<Document>> ListActiveAsync(CancellationToken ct);

    // Not deleted, read-only, only the grid columns.
    Task<IReadOnlyList<DocumentListItem>> SearchAsync(DocumentSearch search, int limit, CancellationToken ct);
    Task<int> CountAsync(DocumentSearch search, CancellationToken ct);

    void Add(Document document);

    // Only to undo an import whose file couldn't be written. Deleting a document is a soft delete.
    void Remove(Document document);
}
