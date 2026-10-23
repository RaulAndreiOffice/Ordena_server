using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Data;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;

namespace Ordena.Backend.Repositories;

internal sealed class DocumentRepository(AppDbContext db) : IDocumentRepository
{
    public Task<Document?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Documents.Include(d => d.Partner).SingleOrDefaultAsync(d => d.Id == id, ct);

    public Task<Document?> GetByHashAsync(string sha256, CancellationToken ct) =>
        db.Documents.SingleOrDefaultAsync(d => d.Sha256 == sha256, ct);

    public Task<bool> NumberExistsAsync(int partnerId, string docNumberNorm, int? exceptId, CancellationToken ct) =>
        db.Documents.AnyAsync(d =>
            d.PartnerId == partnerId &&
            d.DocNumberNorm == docNumberNorm &&
            d.DeletedAt == null &&
            d.Id != exceptId, ct);

    public Task<bool> RelativePathExistsAsync(string relativePath, CancellationToken ct) =>
        db.Documents.AnyAsync(d => d.RelativePath == relativePath, ct);

    public async Task<IReadOnlyList<Document>> ListAllAsync(CancellationToken ct) =>
        await db.Documents.AsNoTracking()
            .Include(d => d.Partner)
            .OrderBy(d => d.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public Task<DateTime?> GetLastUpdateAsync(CancellationToken ct) =>
        db.Documents.MaxAsync(d => (DateTime?)d.UpdatedAt, ct);

    public async Task<IReadOnlyList<DocumentDetails>> ListDeletedAsync(CancellationToken ct) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.DeletedAt != null)
            .OrderByDescending(d => d.DeletedAt).ThenByDescending(d => d.Id)
            .Select(d => new DocumentDetails(
                d.Id, d.Partner.Name, d.DocNumber, d.IssueDate, d.Type, d.Direction,
                d.RelativePath, d.OriginalName, d.ImportedAt, d.UpdatedAt, d.DeletedAt))
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<(string PartnerNameNorm, string DocNumberNorm)>> GetNumberKeysAsync(CancellationToken ct) =>
        (await db.Documents.AsNoTracking()
            .Where(d => d.DeletedAt == null)
            .Select(d => new { d.Partner.NameNorm, d.DocNumberNorm })
            .ToListAsync(ct).ConfigureAwait(false))
        .Select(k => (k.NameNorm, k.DocNumberNorm))
        .ToList();

    public async Task<IReadOnlyList<int>> GetYearsAsync(CancellationToken ct) =>
        await db.Documents.AsNoTracking()
            .Where(d => d.DeletedAt == null)
            .Select(d => d.IssueDate.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<DocumentListItem>> SearchAsync(DocumentSearch search, int limit, CancellationToken ct) =>
        await Sort(Filter(search), search.SortBy, search.Descending)
            .Take(limit)
            .Select(d => new DocumentListItem(
                d.Id, d.IssueDate, d.Partner.Name, d.DocNumber, d.Type, d.Direction, d.RelativePath))
            .ToListAsync(ct).ConfigureAwait(false);

    public Task<int> CountAsync(DocumentSearch search, CancellationToken ct) =>
        Filter(search).CountAsync(ct);

    public void Add(Document document) => db.Documents.Add(document);

    public void Remove(Document document) => db.Documents.Remove(document);

    private IQueryable<Document> Filter(DocumentSearch search)
    {
        var query = db.Documents.AsNoTracking().Where(d => d.DeletedAt == null);

        // Every term must match. The terms are already normalized, so they contain no % or _.
        foreach (var term in search.Terms)
        {
            var name = term.Name.Length > 0 ? $"%{term.Name}%" : null;
            var number = term.Number.Length > 0 ? $"%{term.Number}%" : null;
            DateOnly? yearFrom = term.Year is { } year ? new DateOnly(year, 1, 1) : null;
            DateOnly? yearTo = yearFrom?.AddYears(1);

            query = query.Where(d =>
                (name != null && EF.Functions.Like(d.Partner.NameNorm, name)) ||
                (number != null && EF.Functions.Like(d.DocNumberNorm, number)) ||
                (yearFrom != null && d.IssueDate >= yearFrom && d.IssueDate < yearTo));
        }

        if (search.From is { } from)
            query = query.Where(d => d.IssueDate >= from);
        if (search.ToExclusive is { } to)
            query = query.Where(d => d.IssueDate < to);
        if (search.FirstMonth is { } firstMonth && search.LastMonth is { } lastMonth)
            query = query.Where(d => d.IssueDate.Month >= firstMonth && d.IssueDate.Month <= lastMonth);
        if (search.Type is { } type)
            query = query.Where(d => d.Type == type);
        if (search.Direction is { } direction)
            query = query.Where(d => d.Direction == direction);

        return query;
    }

    private static IQueryable<Document> Sort(IQueryable<Document> query, SearchSort sortBy, bool descending) => sortBy switch
    {
        SearchSort.IssueDate => Order(query, d => d.IssueDate, descending),
        SearchSort.Partner => Order(query, d => d.Partner.NameNorm, descending),
        SearchSort.DocNumber => Order(query, d => d.DocNumberNorm, descending),
        SearchSort.Type => Order(query, d => d.Type, descending),
        SearchSort.Direction => Order(query, d => d.Direction, descending),
        _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, null),
    };

    // Rows with the same key: the newest document first (a partner's invoices by date), then by Id, so
    // they keep the same order from one query to the next.
    private static IQueryable<Document> Order<TKey>(
        IQueryable<Document> query, Expression<Func<Document, TKey>> key, bool descending) =>
        (descending ? query.OrderByDescending(key) : query.OrderBy(key))
            .ThenByDescending(d => d.IssueDate)
            .ThenByDescending(d => d.Id);
}
