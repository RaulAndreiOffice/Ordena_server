using Ordena.Backend.Dtos;
using Ordena.Backend.Models;

namespace Ordena.Backend.Repositories;

// A search already normalized by SearchService, ready to become SQL.
internal sealed record DocumentSearch
{
    public IReadOnlyList<Term> Terms { get; init; } = [];

    // IssueDate range from the Year (and Quarter) filter.
    public DateOnly? From { get; init; }
    public DateOnly? ToExclusive { get; init; }

    // Month range when a quarter is picked without a year.
    public int? FirstMonth { get; init; }
    public int? LastMonth { get; init; }

    public DocType? Type { get; init; }
    public Direction? Direction { get; init; }

    // Only the documents not paid (or collected) yet.
    public bool PendingOnly { get; init; }

    public SearchSort SortBy { get; init; } = SearchSort.IssueDate;
    public bool Descending { get; init; } = true;

    // A term matches the partner name, the document number or its year. Empty parts are skipped.
    public sealed record Term(string Name, string Number, int? Year);
}
