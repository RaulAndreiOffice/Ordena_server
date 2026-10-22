using System.Globalization;
using Ordena.Backend.Models;
using Ordena.Backend.Rules;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Rules;

public sealed class ArchiveNamingTests
{
    private static readonly Partner Telefonica = TestData.NewPartner(); // FolderName "Telefonica-Espana"

    [Fact]
    public void Builds_the_folder_and_the_file_name()
    {
        var path = ArchiveNaming.BuildRelativePath(Doc("A-2026/0123", "2026-03-14"), Telefonica);

        Assert.Equal("2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf", path);
    }

    [Theory]
    [InlineData("2026-01-01", "2026/1T/")]
    [InlineData("2026-03-31", "2026/1T/")]
    [InlineData("2026-04-01", "2026/2T/")]
    [InlineData("2026-09-30", "2026/3T/")]
    [InlineData("2026-10-01", "2026/4T/")]
    [InlineData("2026-12-31", "2026/4T/")]
    public void The_quarter_folder_follows_the_issue_date(string issueDate, string expectedStart)
    {
        var path = ArchiveNaming.BuildRelativePath(Doc("1", issueDate), Telefonica);

        Assert.StartsWith(expectedStart, path);
    }

    [Fact]
    public void An_issued_document_goes_to_Emitidas()
    {
        var document = Doc("F-7", "2026-05-02", direction: Direction.Emitida);

        Assert.Equal(
            "2026/2T/Emitidas/Telefonica-Espana/2026-05-02_Telefonica-Espana_F-7.pdf",
            ArchiveNaming.BuildRelativePath(document, Telefonica));
    }

    [Theory]
    [InlineData(DocType.Factura, "2026-03-14_Telefonica-Espana_T-1.pdf")]
    [InlineData(DocType.FacturaSimplificada, "2026-03-14_Telefonica-Espana_T-1_Ticket.pdf")]
    [InlineData(DocType.FacturaRectificativa, "2026-03-14_Telefonica-Espana_T-1_Rectif.pdf")]
    [InlineData(DocType.Recibo, "2026-03-14_Telefonica-Espana_T-1_Recibo.pdf")]
    [InlineData(DocType.Proforma, "2026-03-14_Telefonica-Espana_T-1_Proforma.pdf")]
    [InlineData(DocType.Otro, "2026-03-14_Telefonica-Espana_T-1_Otro.pdf")]
    public void The_type_gives_the_suffix(DocType type, string expectedFileName)
    {
        var path = ArchiveNaming.BuildRelativePath(Doc("T-1", "2026-03-14", type), Telefonica);

        Assert.Equal(expectedFileName, Path.GetFileName(path));
    }

    // A new enum value without a folder or a suffix fails here instead of at import time.
    [Fact]
    public void Every_type_and_direction_has_a_name()
    {
        foreach (var type in Enum.GetValues<DocType>())
            ArchiveNaming.TypeSuffix(type);
        foreach (var direction in Enum.GetValues<Direction>())
            ArchiveNaming.DirectionFolder(direction);
    }

    [Fact]
    public void An_undefined_type_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArchiveNaming.TypeSuffix((DocType)7));
    }

    [Fact]
    public void The_number_is_cut_to_30_characters()
    {
        var path = ArchiveNaming.BuildRelativePath(Doc("1234567890-1234567890-1234567890", "2026-03-14"), Telefonica);

        Assert.EndsWith("_Telefonica-Espana_1234567890-1234567890-12345678.pdf", path);
    }

    [Theory]
    [InlineData(DocType.Factura, "2026-03-14_Telefonica-Espana.pdf")]
    [InlineData(DocType.FacturaSimplificada, "2026-03-14_Telefonica-Espana_Ticket.pdf")]
    public void A_number_with_nothing_usable_is_left_out(DocType type, string expectedFileName)
    {
        var path = ArchiveNaming.BuildRelativePath(Doc("%%%", "2026-03-14", type), Telefonica);

        Assert.Equal(expectedFileName, Path.GetFileName(path));
    }

    [Fact]
    public void WithCopyNumber_goes_before_the_extension()
    {
        Assert.Equal(
            "2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812_Ticket_2.pdf",
            ArchiveNaming.WithCopyNumber("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812_Ticket.pdf", 2));
    }

    [Fact]
    public void ToFullPath_uses_Windows_separators()
    {
        Assert.Equal(
            @"D:\Facturas\2026\1T\Recibidas\Repsol\a.pdf",
            ArchiveNaming.ToFullPath(@"D:\Facturas", "2026/1T/Recibidas/Repsol/a.pdf"));
    }

    private static Document Doc(
        string docNumber, string issueDate, DocType type = DocType.Factura, Direction direction = Direction.Recibida)
    {
        var document = TestData.NewDocument(
            0, docNumber: docNumber, issueDate: DateOnly.Parse(issueDate, CultureInfo.InvariantCulture));
        document.Type = type;
        document.Direction = direction;
        return document;
    }
}
