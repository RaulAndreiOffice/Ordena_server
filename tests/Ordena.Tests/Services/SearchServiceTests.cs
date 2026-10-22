using System.Globalization;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

public sealed class SearchServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly SearchService _search;

    public SearchServiceTests() => _search = new SearchService(_db.UnitOfWorkFactory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Empty_search_lists_newest_first_and_hides_deleted()
    {
        var telefonica = Partner("Telefónica España, S.A.U.");
        Doc(telefonica, "A-1", "2026-01-10");
        Doc(telefonica, "A-2", "2026-03-14");
        Doc(telefonica, "A-3", "2026-05-01", deleted: true);

        var result = await _search.SearchAsync(new SearchQuery(), Ct);

        Assert.Equal(["A-2", "A-1"], result.Items.Select(i => i.DocNumber));
        Assert.Equal(2, result.TotalCount);
        Assert.False(result.IsTruncated);
    }

    [Theory]
    [InlineData("telefonica")]
    [InlineData("TELEFÓNICA")]
    [InlineData("telef")]
    [InlineData("españa")]
    public async Task Partner_name_matches_ignoring_accents_and_case(string text)
    {
        Doc(Partner("Telefónica España, S.A.U."), "T-1", "2026-03-14");
        Doc(Partner("Repsol S.A."), "R-1", "2026-03-14");

        Assert.Equal(["T-1"], await Numbers(new SearchQuery { Text = text }));
    }

    [Theory]
    [InlineData("0123")]
    [InlineData("2026/0123")]
    [InlineData("a-2026-0123")]
    [InlineData("A20260123")]
    public async Task Number_matches_however_it_is_typed(string text)
    {
        var partner = Partner("Telefónica España, S.A.U.");
        Doc(partner, "A-2026/0123", "2026-03-14");
        Doc(partner, "B-777", "2026-03-14");

        Assert.Equal(["A-2026/0123"], await Numbers(new SearchQuery { Text = text }));
    }

    [Fact]
    public async Task Every_word_must_match()
    {
        var repsol = Partner("Repsol S.A.");
        Doc(repsol, "R-1", "2025-06-01");
        Doc(repsol, "R-2", "2026-02-01");
        Doc(Partner("Telefónica España, S.A.U."), "T-1", "2025-03-01");

        Assert.Equal(["R-1"], await Numbers(new SearchQuery { Text = "repsol 2025" }));
    }

    [Fact]
    public async Task Year_and_quarter_filters_use_the_issue_date()
    {
        var partner = Partner("Telefónica España, S.A.U.");
        Doc(partner, "Q1", "2026-03-31");
        Doc(partner, "Q2", "2026-04-01");
        Doc(partner, "OLD", "2025-02-10");

        Assert.Equal(["Q1"], await Numbers(new SearchQuery { Year = 2026, Quarter = 1 }));
        Assert.Equal(["Q2"], await Numbers(new SearchQuery { Year = 2026, Quarter = 2 }));
        Assert.Equal(["Q2", "Q1"], await Numbers(new SearchQuery { Year = 2026 }));
        // A quarter without a year matches that quarter in every year.
        Assert.Equal(["Q1", "OLD"], await Numbers(new SearchQuery { Quarter = 1 }));
    }

    [Fact]
    public async Task Type_and_direction_filters()
    {
        var partner = Partner("Telefónica España, S.A.U.");
        Doc(partner, "F", "2026-03-14");
        Doc(partner, "E", "2026-03-13", direction: Direction.Emitida);
        Doc(partner, "R", "2026-03-12", type: DocType.Recibo);

        Assert.Equal(["R"], await Numbers(new SearchQuery { Type = DocType.Recibo }));
        Assert.Equal(["E"], await Numbers(new SearchQuery { Direction = Direction.Emitida }));
        Assert.Equal(["F", "R"], await Numbers(new SearchQuery { Direction = Direction.Recibida }));
    }

    [Theory]
    [InlineData("1%3")]
    [InlineData("1_3")]
    public async Task Percent_and_underscore_are_not_wildcards(string text)
    {
        // As LIKE wildcards, "%1%3%" or "%1_3%" would match "A20260123".
        Doc(Partner("Telefónica España, S.A.U."), "A-2026/0123", "2026-03-14");

        Assert.Empty(await Numbers(new SearchQuery { Text = text }));
    }

    [Fact]
    public async Task At_most_500_items_but_the_count_includes_every_match()
    {
        var partner = Partner("Telefónica España, S.A.U.");
        using (var db = _db.CreateContext())
        {
            db.Documents.AddRange(Enumerable.Range(0, 501).Select(_ => TestData.NewDocument(partner)));
            db.SaveChanges();
        }

        var result = await _search.SearchAsync(new SearchQuery(), Ct);

        Assert.Equal(SearchService.MaxResults, result.Items.Count);
        Assert.Equal(501, result.TotalCount);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task Sorts_in_the_database_by_the_chosen_column()
    {
        Doc(Partner("Zeta"), "Z", "2026-01-01");
        Doc(Partner("Alfa"), "A", "2026-02-01");
        Doc(Partner("Mu"), "M", "2026-03-01");

        var ascending = await _search.SearchAsync(new SearchQuery { SortBy = SearchSort.Partner, Descending = false }, Ct);
        var descending = await _search.SearchAsync(new SearchQuery { SortBy = SearchSort.Partner }, Ct);

        Assert.Equal(["Alfa", "Mu", "Zeta"], ascending.Items.Select(i => i.PartnerName));
        Assert.Equal(["Zeta", "Mu", "Alfa"], descending.Items.Select(i => i.PartnerName));
    }

    [Fact]
    public async Task Returns_the_grid_columns()
    {
        var partner = Partner("Telefónica España, S.A.U.");
        var document = Doc(partner, "A-2026/0123", "2026-03-14", DocType.FacturaRectificativa, Direction.Emitida);

        var item = Assert.Single((await _search.SearchAsync(new SearchQuery(), Ct)).Items);

        Assert.Equal(
            new DocumentListItem(document.Id, new DateOnly(2026, 3, 14), "Telefónica España, S.A.U.",
                "A-2026/0123", DocType.FacturaRectificativa, Direction.Emitida, document.RelativePath),
            item);
    }

    [Fact]
    public async Task A_quarter_outside_1_to_4_throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _search.SearchAsync(new SearchQuery { Quarter = 5 }, Ct));
    }

    private int Partner(string name) =>
        _db.Save(TestData.NewPartner(Guid.NewGuid().ToString("N"), name)).Id;

    private Document Doc(
        int partnerId, string number, string date,
        DocType type = DocType.Factura, Direction direction = Direction.Recibida, bool deleted = false)
    {
        var document = TestData.NewDocument(
            partnerId, docNumber: number, issueDate: DateOnly.Parse(date, CultureInfo.InvariantCulture));
        document.Type = type;
        document.Direction = direction;
        document.DeletedAt = deleted ? TestData.Now : null;
        return _db.Save(document);
    }

    private async Task<string[]> Numbers(SearchQuery query) =>
        (await _search.SearchAsync(query, Ct)).Items.Select(i => i.DocNumber).ToArray();
}
