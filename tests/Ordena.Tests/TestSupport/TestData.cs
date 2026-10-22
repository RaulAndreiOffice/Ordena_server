using Ordena.Backend.Models;
using Ordena.Backend.Rules;

namespace Ordena.Tests.TestSupport;

// Valid entities with every required column filled, so a test only sets what it is about.
// The normalized columns use the real TextNormalizer, exactly like production data.
internal static class TestData
{
    private static int _next;

    public static readonly DateTime Now = new(2026, 3, 14, 10, 30, 15, 123, DateTimeKind.Utc);

    public static Partner NewPartner(string folderName = "Telefonica-Espana", string name = "Telefónica España, S.A.U.") => new()
    {
        Name = name,
        NameNorm = TextNormalizer.NormalizeName(name),
        FolderName = folderName,
        CreatedAt = Now,
    };

    // The default number contains the counter; search tests pass their own numbers.
    public static Document NewDocument(
        int partnerId,
        string? sha256 = null,
        string? relativePath = null,
        string? docNumber = null,
        DateOnly? issueDate = null)
    {
        var n = Interlocked.Increment(ref _next);
        docNumber ??= $"A-2026/{n:0000}";
        return new Document
        {
            PartnerId = partnerId,
            Sha256 = sha256 ?? n.ToString("x64"),
            DocNumber = docNumber,
            DocNumberNorm = TextNormalizer.NormalizeNumber(docNumber),
            IssueDate = issueDate ?? new DateOnly(2026, 3, 14),
            Type = DocType.Factura,
            Direction = Direction.Recibida,
            RelativePath = relativePath ?? $"2026/1T/Recibidas/Partner/2026-03-14_Partner_A-2026-{n:0000}.pdf",
            OriginalName = $"scan{n}.pdf",
            FileSizeBytes = 120_000,
            ImportedAt = Now,
            UpdatedAt = Now,
        };
    }

    public static FileOperation NewFileOperation(int documentId) => new()
    {
        DocumentId = documentId,
        FromPath = @"C:\Users\me\Downloads\scan.pdf",
        ToPath = "2026/1T/Recibidas/Partner/file.pdf",
        State = FileOperationState.Pending,
        CreatedAt = Now,
    };
}
