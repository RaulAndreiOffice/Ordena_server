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
    public void The_values_overload_gives_the_same_path()
    {
        var document = Doc("R-15", "2026-12-31", DocType.Recibo, Direction.Emitida);

        Assert.Equal(
            ArchiveNaming.BuildRelativePath(document, Telefonica),
            ArchiveNaming.BuildRelativePath(
                document.IssueDate, document.Direction, document.Type, document.DocNumber, Telefonica.FolderName));
    }

    [Fact]
    public void ToTrash_keeps_the_folders_under_Papelera()
    {
        Assert.Equal(
            "_Papelera/2026/1T/Recibidas/Repsol/a.pdf",
            ArchiveNaming.ToTrash("2026/1T/Recibidas/Repsol/a.pdf"));
    }

    public static TheoryData<DocType> AllTypes => new(Enum.GetValues<DocType>());

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void TryParse_reads_back_what_BuildRelativePath_wrote(DocType type)
    {
        var path = ArchiveNaming.BuildRelativePath(Doc("A-2026/0123", "2026-05-02", type, Direction.Emitida), Telefonica);

        Assert.Equal(
            new ArchivePath(new DateOnly(2026, 5, 2), Direction.Emitida, type, "Telefonica-Espana", "A-2026-0123", InTrash: false),
            ArchiveNaming.TryParse(path));
    }

    [Theory]
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812_Ticket_2.pdf", "T-88812", DocType.FacturaSimplificada, false)]
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812_2.pdf", "T-88812", DocType.Factura, false)]
    [InlineData("_Papelera/2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812.pdf", "T-88812", DocType.Factura, true)]
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol.pdf", "", DocType.Factura, false)]
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_Ticket.pdf", "", DocType.FacturaSimplificada, false)]
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_123.pdf", "123", DocType.Factura, false)]
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_123_4.pdf", "123", DocType.Factura, false)]
    public void TryParse_reads_copy_numbers_the_trash_and_missing_parts(
        string path, string number, DocType type, bool inTrash)
    {
        var parsed = ArchiveNaming.TryParse(path);

        Assert.Equal((number, type, inTrash, "Repsol"), (parsed?.DocNumber, parsed?.Type, parsed?.InTrash, parsed?.PartnerFolder));
    }

    [Theory]
    [InlineData("2026/2T/Recibidas/Repsol/2026-03-20_Repsol_1.pdf")] // quarter folder doesn't match the date
    [InlineData("2025/1T/Recibidas/Repsol/2026-03-20_Repsol_1.pdf")] // year folder doesn't match
    [InlineData("2026/1T/Pagadas/Repsol/2026-03-20_Repsol_1.pdf")] // unknown direction
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Cepsa_1.pdf")] // another partner in the name
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_RepsolX_1.pdf")]
    [InlineData("2026/1T/Recibidas/Repsol/2026-02-30_Repsol_1.pdf")] // no such date
    [InlineData("2026/1T/Recibidas/Repsol/20-03-2026_Repsol_1.pdf")]
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_1.docx")]
    [InlineData("2026/1T/Repsol/2026-03-20_Repsol_1.pdf")] // a folder missing
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_A-1_B-2.pdf")] // two numbers
    [InlineData("2026/1T/Recibidas/Repsol/2026-03-20_Repsol_A-1__2.pdf")]
    [InlineData("escaneo.pdf")]
    public void TryParse_rejects_paths_the_app_would_not_write(string path)
    {
        Assert.Null(ArchiveNaming.TryParse(path));
    }

    [Theory]
    [InlineData(@"D:\Facturas", @"D:\Facturas", true)]
    [InlineData(@"D:\Facturas", @"D:\facturas\", true)]
    [InlineData(@"D:\Facturas", @"D:\Facturas\2026", true)]
    [InlineData(@"D:\Facturas\2026", @"D:\Facturas", true)]
    [InlineData(@"E:\", @"E:\Facturas", true)]
    [InlineData(@"D:\Facturas", @"D:\Facturas-nuevas", false)]
    [InlineData(@"D:\Facturas", @"E:\Facturas", false)]
    public void AreNested(string folder, string otherFolder, bool expected)
    {
        Assert.Equal(expected, ArchiveNaming.AreNested(folder, otherFolder));
    }

    [Fact]
    public void IsInArchive_works_for_a_whole_disk_and_not_for_a_sibling_folder()
    {
        Assert.True(ArchiveNaming.IsInArchive(@"E:\", @"E:\2026\a.pdf"));
        Assert.False(ArchiveNaming.IsInArchive(@"D:\Facturas", @"D:\Facturas-nuevas\a.pdf"));
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
