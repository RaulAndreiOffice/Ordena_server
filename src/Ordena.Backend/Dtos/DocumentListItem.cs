using Ordena.Backend.Models;

namespace Ordena.Backend.Dtos;

// One row of the search grid: only the columns it shows, never the full entity.
// IsPaid false is pending (to pay or to collect): Buscar lists those first.
public sealed record DocumentListItem(
    int Id,
    DateOnly IssueDate,
    string PartnerName,
    string DocNumber,
    DocType Type,
    Direction Direction,
    string RelativePath,
    bool IsPaid = true);
