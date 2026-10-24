namespace Ordena.Backend.Dtos;

// Items holds at most the first 500 matches, the pending ones first; TotalCount is every match, for the
// status bar. PendingTotal counts every pending document, whatever the search: the reminder above the grid.
public sealed record SearchResult(IReadOnlyList<DocumentListItem> Items, int TotalCount, int PendingTotal = 0)
{
    public bool IsTruncated => TotalCount > Items.Count;
}
