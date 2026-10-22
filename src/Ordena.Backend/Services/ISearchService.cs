using Ordena.Backend.Dtos;

namespace Ordena.Backend.Services;

public interface ISearchService
{
    // Deleted documents are never included. At most 500 items; TotalCount counts every match.
    // Throws ArgumentOutOfRangeException when Quarter is not 1 to 4.
    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct);
}
