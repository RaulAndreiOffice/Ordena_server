using System.Globalization;
using Ordena.Backend.Results;
using Ordena.Backend.Rules;

namespace Ordena.Tests.Rules;

public sealed class FileNamePatternTests
{
    private static readonly DateOnly March14 = new(2026, 3, 14);

    public static TheoryData<string, string, string> EveryPresetSeparatorAndFormat()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var preset in new[] { "{fecha}{s}{proveedor}{s}{numero}", "{proveedor}{s}{numero}{s}{fecha}", "{numero}{s}{proveedor}{s}{fecha}" })
        {
            foreach (var separator in new[] { "_", "-", " ", "." })
            {
                foreach (var format in FileNamePattern.DateFormats)
                    data.Add(preset, separator, format);
            }
        }
        return data;
    }

    // Each preset, written with each separator and date format, reads back its own parts.
    [Theory]
    [MemberData(nameof(EveryPresetSeparatorAndFormat))]
    public void Every_preset_reads_names_written_with_it(string preset, string separator, string format)
    {
        var pattern = Create(preset.Replace("{s}", separator), format);
        var name = preset.Replace("{s}", separator)
            .Replace("{fecha}", March14.ToString(format, CultureInfo.InvariantCulture))
            .Replace("{proveedor}", "Repsol")
            .Replace("{numero}", "T88812");

        Assert.Equal(new ParsedName(NameMatch.Ok, March14, "Repsol", "T88812"), pattern.Match(name));
    }

    [Theory]
    [InlineData("2026-03-14_Telefonica_Espana_A-2026-0123", "Telefonica Espana", "A-2026-0123")]
    [InlineData("2026-03-14_Telefonica Espana SAU_A-0123", "Telefonica Espana SAU", "A-0123")]
    [InlineData("2026-03-14_Repsol S.A._F-77", "Repsol S.A.", "F-77")]
    public void Separators_inside_the_supplier_name_stay_in_it(string name, string partner, string number)
    {
        var parsed = Create("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd").Match(name);

        Assert.Equal(new ParsedName(NameMatch.Ok, March14, partner, number), parsed);
    }

    [Fact]
    public void A_number_at_the_start_is_its_first_part()
    {
        var parsed = Create("{numero}_{proveedor}_{fecha}", "ddMMyyyy").Match("A-1_Repsol_Butano_14032026");

        Assert.Equal(new ParsedName(NameMatch.Ok, March14, "Repsol Butano", "A-1"), parsed);
    }

    [Theory]
    [InlineData("2026-02-30_Repsol_A-1")]
    [InlineData("2026-13-01_Repsol_A-1")]
    [InlineData("0000-01-01_Repsol_A-1")]
    public void A_date_that_does_not_exist_is_InvalidDate_with_the_rest_still_read(string name)
    {
        var parsed = Create("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd").Match(name);

        Assert.Equal(new ParsedName(NameMatch.InvalidDate, null, "Repsol", "A-1"), parsed);
    }

    [Theory]
    [InlineData("escaneo 001")]
    [InlineData("2026-03-14_Repsol")]
    [InlineData("14-03-2026_Repsol_A-1")]
    [InlineData("2026-03-14__A-1")]
    public void A_name_of_another_shape_is_not_recognized(string name)
    {
        Assert.Equal(NameMatch.NotRecognized, Create("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd").Match(name).Status);
    }

    [Fact]
    public void Without_a_date_the_pattern_reads_partner_and_number()
    {
        var pattern = Create("{proveedor} - {numero}", null);

        Assert.False(pattern.HasDate);
        Assert.Equal(new ParsedName(NameMatch.Ok, null, "Repsol S.A", "A-12"), pattern.Match("Repsol S.A  -  A-12"));
    }

    [Fact]
    public void Tokens_ignore_case()
    {
        Assert.Equal(NameMatch.Ok, Create("{Fecha}_{PROVEEDOR}_{numero}", "yyyy-MM-dd").Match("2026-03-14_Repsol_1").Status);
    }

    // With one folder per supplier the name needs no partner.
    [Fact]
    public void The_partner_may_be_left_to_the_folder()
    {
        var pattern = FileNamePattern.Create("{fecha}_{numero}", "yyyy-MM-dd", partnerFromFolder: true).Value;

        Assert.Equal(new ParsedName(NameMatch.Ok, March14, null, "A-1"), pattern.Match("2026-03-14_A-1"));
    }

    [Theory]
    [InlineData("{fecha}_{proveedor}_{importe}", "yyyy-MM-dd", "UnknownToken")]
    [InlineData("{fecha}_{numero}_{numero}", "yyyy-MM-dd", "RepeatedToken")]
    [InlineData("{fecha}_{proveedor}", "yyyy-MM-dd", "MissingNumber")]
    [InlineData("{fecha}_{numero}", "yyyy-MM-dd", "MissingPartner")]
    [InlineData("{fecha}{proveedor}_{numero}", "yyyy-MM-dd", "TokensTogether")]
    [InlineData("{fecha}_{proveedor}_{numero}", "MM/dd/yyyy", "UnknownDateFormat")]
    [InlineData("{fecha}_{proveedor}_{numero}", null, "UnknownDateFormat")]
    public void A_pattern_that_cannot_be_used_says_why(string pattern, string? format, string reason)
    {
        var created = FileNamePattern.Create(pattern, format, partnerFromFolder: false);

        Assert.Equal(ErrorCode.InvalidNamePattern, created.Error);
        Assert.Equal(reason, created.Detail);
    }

    [Theory]
    [InlineData("Telefonica_Espana", "_", "Telefonica Espana")]
    [InlineData(" Repsol  S.A. ", "_", "Repsol S.A.")]
    [InlineData("Iberdrola.Clientes", ".", "Iberdrola Clientes")]
    [InlineData("-Iberdrola-", "-", "Iberdrola")]
    public void Partner_names_are_cleaned_up(string raw, string separators, string expected)
    {
        Assert.Equal(expected, FileNamePattern.CleanPartner(raw, separators));
    }

    // With "." as the separator, a dot inside the name is one too.
    [Fact]
    public void The_patterns_own_separator_becomes_a_space_in_the_name()
    {
        var parsed = Create("{fecha}.{proveedor}.{numero}", "dd-MM-yyyy").Match("14-03-2026.Iberdrola.Clientes.F-77");

        Assert.Equal(new ParsedName(NameMatch.Ok, March14, "Iberdrola Clientes", "F-77"), parsed);
    }

    private static FileNamePattern Create(string pattern, string? format) =>
        FileNamePattern.Create(pattern, format, partnerFromFolder: false).Value;
}
