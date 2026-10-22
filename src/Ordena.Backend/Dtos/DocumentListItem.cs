using Ordena.Backend.Models;

namespace Ordena.Backend.Dtos;

// One row of the search grid: only the columns it shows, never the full entity.
public sealed record DocumentListItem(
    int Id,
    DateOnly IssueDate,
    string PartnerName,
    string DocNumber,
    DocType Type,
    Direction Direction,
    string RelativePath);
