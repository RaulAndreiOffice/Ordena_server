using System.Globalization;
using Ordena.Backend.Rules;

namespace Ordena.Tests.Rules;

public sealed class QuarterTests
{
    [Theory]
    [InlineData("2026-01-01", 1)]
    [InlineData("2026-03-31", 1)]
    [InlineData("2026-04-01", 2)]
    [InlineData("2026-06-30", 2)]
    [InlineData("2026-07-01", 3)]
    [InlineData("2026-09-30", 3)]
    [InlineData("2026-10-01", 4)]
    [InlineData("2026-12-31", 4)]
    public void Of(string date, int expected) =>
        Assert.Equal(expected, Quarter.Of(Date(date)));

    [Theory]
    [InlineData(1, "2026-01-01", "2026-04-01")]
    [InlineData(2, "2026-04-01", "2026-07-01")]
    [InlineData(4, "2026-10-01", "2027-01-01")]
    [InlineData(null, "2026-01-01", "2027-01-01")]
    public void Range(int? quarter, string from, string toExclusive) =>
        Assert.Equal((Date(from), Date(toExclusive)), Quarter.Range(2026, quarter));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void A_quarter_outside_1_to_4_throws(int quarter) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Quarter.Months(quarter));

    private static DateOnly Date(string text) => DateOnly.Parse(text, CultureInfo.InvariantCulture);
}
