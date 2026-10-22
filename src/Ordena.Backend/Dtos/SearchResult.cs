namespace Ordena.Backend.Dtos;

// Items holds at most the first 500 matches; TotalCount is every match, for the status bar.
public sealed record SearchResult(IReadOnlyList<DocumentListItem> Items, int TotalCount)
{
    public bool IsTruncated => TotalCount > Items.Count;
}
