using System.Text;
using Ordena.Backend.Rules;

namespace Ordena.Tests.Rules;

public sealed class PdfSignatureTests
{
    [Fact]
    public void A_pdf_header_at_the_start_is_a_pdf()
    {
        Assert.True(PdfSignature.IsPdf("%PDF-1.4\n"u8));
    }

    [Fact]
    public void A_header_after_some_junk_is_still_a_pdf()
    {
        Assert.True(PdfSignature.IsPdf([.. new byte[100], .. "%PDF-1.4\n"u8]));
    }

    [Fact]
    public void A_header_after_the_first_kilobyte_is_not_a_pdf()
    {
        Assert.False(PdfSignature.IsPdf([.. new byte[1024], .. "%PDF-1.4\n"u8]));
    }

    [Fact]
    public void Other_files_are_not_pdfs()
    {
        Assert.False(PdfSignature.IsPdf([0xFF, 0xD8, 0xFF, 0xE0])); // JPEG
        Assert.False(PdfSignature.IsPdf(Encoding.ASCII.GetBytes("%PDF")));
        Assert.False(PdfSignature.IsPdf([]));
    }
}
