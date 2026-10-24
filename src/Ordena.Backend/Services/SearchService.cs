using Ordena.Backend.Dtos;
using Ordena.Backend.Repositories;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

internal sealed class SearchService(IUnitOfWorkFactory uowFactory) : ISearchService
{
    // The grid shows at most this many rows; the status bar shows the full count.
    public const int MaxResults = 500;

    public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        var search = ToDocumentSearch(query);

        return Task.Run(async () =>
        {
            await using var uow = uowFactory.Create();
            var items = await uow.Documents.SearchAsync(search, MaxResults, ct).ConfigureAwait(false);

            // Fewer rows than the limit means that is every match, so the COUNT query isn't needed.
            var total = items.Count < MaxResults
                ? items.Count
                : await uow.Documents.CountAsync(search, ct).ConfigureAwait(false);
            var pending = await uow.Documents.CountPendingAsync(ct).ConfigureAwait(false);

            return new SearchResult(items, total, pending);
        }, ct);
    }

    public Task<IReadOnlyList<int>> GetYearsAsync(CancellationToken ct) => Task.Run(async () =>
    {
        await using var uow = uowFactory.Create();
        return await uow.Documents.GetYearsAsync(ct).ConfigureAwait(false);
    }, ct);

    private static DocumentSearch ToDocumentSearch(SearchQuery query)
    {
        DateOnly? from = null, toExclusive = null;
        int? firstMonth = null, lastMonth = null;

        if (query.Year is { } year)
            (from, toExclusive) = Quarter.Range(year, query.Quarter);
        else if (query.Quarter is { } quarter)
            (firstMonth, lastMonth) = Quarter.Months(quarter);

        return new DocumentSearch
        {
            Terms = SearchTerms.Parse(query.Text)
                .Select(t => new DocumentSearch.Term(t.Name, t.Number, t.Year))
                .ToList(),
            From = from,
            ToExclusive = toExclusive,
            FirstMonth = firstMonth,
            LastMonth = lastMonth,
            Type = query.Type,
            Direction = query.Direction,
            PendingOnly = query.PendingOnly,
            SortBy = query.SortBy,
            Descending = query.Descending,
        };
    }
}
