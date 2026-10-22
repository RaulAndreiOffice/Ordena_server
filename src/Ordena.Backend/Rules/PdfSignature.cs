namespace Ordena.Backend.Rules;

internal static class PdfSignature
{
    // Like Acrobat, the "%PDF-" header may come after up to 1 KB of other bytes:
    // some scanners and mail programs put something before it.
    private const int SearchLength = 1024;

    public static bool IsPdf(ReadOnlySpan<byte> content) =>
        content[..Math.Min(content.Length, SearchLength)].IndexOf("%PDF-"u8) >= 0;
}
