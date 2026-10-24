using Ordena.Backend.Dtos;
using Ordena.Backend.Models;

namespace Ordena.Backend.Services;

// Entity → DTO for documents loaded with their Partner.
internal static class DocumentMapping
{
    public static DocumentListItem ToListItem(this Document d) => new(
        d.Id, d.IssueDate, d.Partner.Name, d.DocNumber, d.Type, d.Direction, d.RelativePath, d.IsPaid);

    public static DocumentDetails ToDetails(this Document d) => new(
        d.Id, d.Partner.Name, d.DocNumber, d.IssueDate, d.Type, d.Direction,
        d.RelativePath, d.OriginalName, d.ImportedAt, d.UpdatedAt, d.DeletedAt, d.IsPaid);
}
