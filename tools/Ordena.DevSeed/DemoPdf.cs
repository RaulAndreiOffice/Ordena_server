using System.Globalization;
using System.Text;
using Ordena.Backend.Models;

namespace Ordena.DevSeed;

// A one-page invoice PDF written by hand (Helvetica, no library): enough for the preview to show which
// document it is. The marker makes every file's bytes, and so its hash, unique.
internal static class DemoPdf
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static byte[] Create(
        DocType type, string partner, string number, DateOnly date, string concept, decimal amount, string marker)
    {
        var total = amount.ToString("N2", Spanish);
        string[] page =
        [
            "0.459 0.224 1 rg 0 782 595 60 re f",
            $"BT /F2 22 Tf 1 1 1 rg 50 805 Td ({Text(Title(type))}) Tj ET",
            $"BT /F2 14 Tf 0.02 0.07 0.13 rg 50 740 Td ({Text(partner)}) Tj ET",
            $"BT /F1 12 Tf 0.02 0.07 0.13 rg 360 740 Td (Nº {Text(number)}) Tj ET",
            $"BT /F1 12 Tf 0.02 0.07 0.13 rg 360 722 Td (Fecha: {date.ToString("dd/MM/yyyy", Spanish)}) Tj ET",
            "0.89 0.89 0.9 RG 1 w 50 690 m 545 690 l S",
            $"BT /F1 12 Tf 0.02 0.07 0.13 rg 50 660 Td ({Text(concept)}) Tj 400 0 Td ({total} EUR) Tj ET",
            "0.89 0.89 0.9 RG 50 630 m 545 630 l S",
            $"BT /F2 14 Tf 0.02 0.07 0.13 rg 360 600 Td (Total: {total} EUR) Tj ET",
            $"BT /F1 7 Tf 0.6 0.6 0.6 rg 50 40 Td (Ordena demo {Text(marker)}) Tj ET",
        ];
        var content = Encoding.Latin1.GetBytes(string.Join("\n", page));

        byte[][] objects =
        [
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>"),
            [.. Ascii($"<< /Length {content.Length} >>\nstream\n"), .. content, .. Ascii("\nendstream")],
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
        ];

        using var pdf = new MemoryStream();
        Write(pdf, "%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Position);
            Write(pdf, $"{i + 1} 0 obj\n");
            pdf.Write(objects[i]);
            Write(pdf, "\nendobj\n");
        }

        var xref = pdf.Position;
        Write(pdf, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write(pdf, $"{offset:D10} 00000 n \n");
        Write(pdf, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static string Title(DocType type) => type switch
    {
        DocType.FacturaSimplificada => "FACTURA SIMPLIFICADA",
        DocType.FacturaRectificativa => "FACTURA RECTIFICATIVA",
        DocType.Recibo => "RECIBO",
        DocType.Proforma => "PROFORMA",
        DocType.Otro => "DOCUMENTO",
        _ => "FACTURA",
    };

    // A PDF string literal: backslash and parentheses escaped; Latin-1 covers á, é, ñ… (WinAnsi).
    private static string Text(string value) =>
        value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void Write(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));
}
