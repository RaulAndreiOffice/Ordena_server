using System.Text;

namespace Ordena.Tests.TestSupport;

internal static class TestPdf
{
    // Enough of a PDF for the header check. A different marker gives a different hash.
    public static byte[] Bytes(string marker) =>
        Encoding.ASCII.GetBytes($"%PDF-1.7\n% {marker}\n%%EOF\n");
}
