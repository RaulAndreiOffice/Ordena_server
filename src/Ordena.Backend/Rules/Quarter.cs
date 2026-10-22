namespace Ordena.Backend.Rules;

// The quarter of the IVA filing (Modelo 303): Jan–Mar → 1, Apr–Jun → 2, Jul–Sep → 3, Oct–Dec → 4.
internal static class Quarter
{
    public static int Of(DateOnly date) => (date.Month - 1) / 3 + 1;

    public static (int First, int Last) Months(int quarter)
    {
        if (quarter is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(quarter), quarter, "A quarter is 1 to 4.");

        var first = (quarter - 1) * 3 + 1;
        return (first, first + 2);
    }

    // [From, ToExclusive) of the quarter, or of the whole year when quarter is null.
    // A date range lets SQL use the IssueDate index.
    public static (DateOnly From, DateOnly ToExclusive) Range(int year, int? quarter)
    {
        if (quarter is not { } q)
            return (new DateOnly(year, 1, 1), new DateOnly(year + 1, 1, 1));

        var from = new DateOnly(year, Months(q).First, 1);
        return (from, from.AddMonths(3));
    }
}
