using Ordena.Backend.Models;

namespace Ordena.Backend.Dtos;

public sealed record SearchQuery
{
    // Free text: every word must match the partner name, the document number or the year.
    public string? Text { get; init; }

    public int? Year { get; init; }

    // 1 to 4. Without a Year it matches that quarter in every year.
    public int? Quarter { get; init; }

    public DocType? Type { get; init; }
    public Direction? Direction { get; init; }

    // «Ver solo pendientes»: only the documents not paid (or collected) yet.
    public bool PendingOnly { get; init; }

    public SearchSort SortBy { get; init; } = SearchSort.IssueDate;
    public bool Descending { get; init; } = true;
}
