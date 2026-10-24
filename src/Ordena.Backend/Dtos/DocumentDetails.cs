using Ordena.Backend.Models;

namespace Ordena.Backend.Dtos;

// Everything about one document: fills the edit dialog, and the rows of the trash.
public sealed record DocumentDetails(
    int Id,
    string PartnerName,
    string DocNumber,
    DateOnly IssueDate,
    DocType Type,
    Direction Direction,
    string RelativePath,
    string OriginalName,
    DateTime ImportedAt,
    DateTime UpdatedAt,
    DateTime? DeletedAt,
    bool IsPaid = true);
