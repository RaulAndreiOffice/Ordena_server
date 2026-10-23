using Ordena.Backend.Dtos;
using Ordena.Backend.Rules;

namespace Ordena.Tests.Rules;

public sealed class PatternDetectorTests
{
    // Mostly one layout, a few of another, and some names of no layout at all.
    [Fact]
    public void The_pattern_reading_most_names_comes_first()
    {
        string[] names =
        [
            .. Enumerable.Range(1, 20).Select(i => $"{i:D2}-03-2026_Repsol_T-{i}"),
            .. Enumerable.Range(1, 5).Select(i => $"Iberdrola_F-{i}_2025-12-{i:D2}"),
            "escaneo", "IMG_0001", "factura nueva",
        ];

        var ranked = PatternDetector.Rank(names, partnerFromFolder: false);

        Assert.Equal(new NamePattern("{fecha}_{proveedor}_{numero}", "dd-MM-yyyy", 20, 28), ranked[0]);
        Assert.Contains(new NamePattern("{proveedor}_{numero}_{fecha}", "yyyy-MM-dd", 5, 28), ranked);
        Assert.True(ranked.Count <= 3);
        Assert.True(ranked.Zip(ranked.Skip(1)).All(p => p.First.Matches >= p.Second.Matches));
    }

    // 20260314 fits both yyyyMMdd and ddMMyyyy in shape; only one gives real dates.
    [Fact]
    public void Real_dates_decide_between_formats_of_the_same_shape()
    {
        string[] names = ["20260314_Repsol_1", "20251231_Repsol_2", "20240229_Repsol_3"];

        Assert.Equal("yyyyMMdd", PatternDetector.Rank(names, partnerFromFolder: false)[0].DateFormat);
    }

    [Fact]
    public void Names_without_a_date_fall_to_the_undated_preset()
    {
        string[] names = ["Repsol S.A. - T-1", "Iberdrola - F 77"];

        Assert.Equal(new NamePattern("{proveedor} - {numero}", null, 2, 2), PatternDetector.Rank(names, partnerFromFolder: false)[0]);
    }

    [Fact]
    public void With_one_folder_per_supplier_date_and_number_are_enough()
    {
        string[] names = ["2026-03-14_A-1", "2026-03-15_A-2"];

        Assert.DoesNotContain(PatternDetector.Rank(names, partnerFromFolder: false), p => p.Pattern == "{fecha}_{numero}");
        Assert.Equal(new NamePattern("{fecha}_{numero}", "yyyy-MM-dd", 2, 2), PatternDetector.Rank(names, partnerFromFolder: true)[0]);
    }

    [Fact]
    public void Nothing_readable_gives_no_pattern()
    {
        Assert.Empty(PatternDetector.Rank(["escaneo", "IMG_0001"], partnerFromFolder: false));
    }
}
