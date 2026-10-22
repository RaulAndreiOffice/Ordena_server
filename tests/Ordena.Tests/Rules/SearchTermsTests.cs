using Ordena.Backend.Rules;

namespace Ordena.Tests.Rules;

public sealed class SearchTermsTests
{
    [Fact]
    public void Splits_on_whitespace_and_normalizes_each_word()
    {
        var terms = SearchTerms.Parse("  Telefónica-España   2026/0123 ");

        Assert.Equal<SearchTerm>(
        [
            new SearchTerm("telefonica espana", "TELEFONICAESPANA", null),
            new SearchTerm("2026 0123", "20260123", null),
        ], terms);
    }

    [Fact]
    public void A_four_digit_year_also_matches_the_issue_year()
    {
        Assert.Equal<SearchTerm>([new SearchTerm("2026", "2026", 2026)], SearchTerms.Parse("2026"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("% _ / -")]
    public void Words_with_nothing_left_are_dropped(string? text) =>
        Assert.Empty(SearchTerms.Parse(text));
}
