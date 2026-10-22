namespace Ordena.Backend.Dtos;

// Sorting runs in SQL: the grid only holds the first 500 rows, so sorting them in memory would be wrong.
public enum SearchSort
{
    IssueDate,
    Partner,
    DocNumber,
    Type,
    Direction,
}
