using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Data;

// Each rule must be enforced by SQLite itself, not only by the services.
public sealed class ConstraintTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Same_sha256_is_rejected()
    {
        var partner = _db.Save(TestData.NewPartner());
        var sha256 = new string('b', 64);
        _db.Save(TestData.NewDocument(partner.Id, sha256: sha256));

        AssertDb.Rejects(
            () => _db.Save(TestData.NewDocument(partner.Id, sha256: sha256)),
            "UNIQUE constraint failed: Documents.Sha256");
    }

    [Fact]
    public void Same_sha256_is_rejected_even_when_the_first_document_is_deleted()
    {
        var partner = _db.Save(TestData.NewPartner());
        var sha256 = new string('c', 64);
        var deleted = TestData.NewDocument(partner.Id, sha256: sha256);
        deleted.DeletedAt = TestData.Now;
        _db.Save(deleted);

        AssertDb.Rejects(
            () => _db.Save(TestData.NewDocument(partner.Id, sha256: sha256)),
            "UNIQUE constraint failed: Documents.Sha256");
    }

    [Theory]
    [InlineData("Repsol")]
    [InlineData("repsol")]
    [InlineData("REPSOL")]
    public void Same_folder_name_is_rejected_ignoring_case(string folderName)
    {
        _db.Save(TestData.NewPartner("Repsol"));

        AssertDb.Rejects(
            () => _db.Save(TestData.NewPartner(folderName)),
            "UNIQUE constraint failed: Partners.FolderName");
    }

    [Fact]
    public void Same_relative_path_is_rejected_ignoring_case()
    {
        var partner = _db.Save(TestData.NewPartner());
        _db.Save(TestData.NewDocument(partner.Id, relativePath: "2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812.pdf"));

        AssertDb.Rejects(
            () => _db.Save(TestData.NewDocument(partner.Id, relativePath: "2026/1T/RECIBIDAS/repsol/2026-03-20_REPSOL_t-88812.PDF")),
            "UNIQUE constraint failed: Documents.RelativePath");
    }

    [Fact]
    public void Same_partner_and_number_is_allowed()
    {
        // A repeated number is a warning the user can confirm, so the database must not block it.
        var partner = _db.Save(TestData.NewPartner());
        var first = TestData.NewDocument(partner.Id);
        var second = TestData.NewDocument(partner.Id);
        first.DocNumberNorm = second.DocNumberNorm = "A20260123";

        _db.Save(first);
        _db.Save(second);

        using var db = _db.CreateContext();
        Assert.Equal(2, db.Documents.Count(d => d.DocNumberNorm == "A20260123"));
    }

    [Fact]
    public void Document_with_unknown_partner_is_rejected()
    {
        // Also proves SQLite's foreign_keys is on; without it this insert would succeed.
        AssertDb.Rejects(
            () => _db.Save(TestData.NewDocument(partnerId: 999)),
            "FOREIGN KEY constraint failed");
    }

    [Fact]
    public void Journal_row_for_unknown_document_is_rejected()
    {
        AssertDb.Rejects(
            () => _db.Save(TestData.NewFileOperation(documentId: 999)),
            "FOREIGN KEY constraint failed");
    }

    [Fact]
    public void Deleting_a_partner_with_documents_is_rejected()
    {
        var partner = _db.Save(TestData.NewPartner());
        _db.Save(TestData.NewDocument(partner.Id));

        // Only the partner is loaded, so EF sends the DELETE and SQLite has to refuse it.
        AssertDb.Rejects(() =>
        {
            using var db = _db.CreateContext();
            db.Partners.Remove(db.Partners.Single());
            db.SaveChanges();
        }, "FOREIGN KEY constraint failed");
    }

    [Fact]
    public void Deleting_a_document_with_journal_rows_is_rejected()
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = _db.Save(TestData.NewDocument(partner.Id));
        _db.Save(TestData.NewFileOperation(document.Id));

        AssertDb.Rejects(() =>
        {
            using var db = _db.CreateContext();
            db.Documents.Remove(db.Documents.Single());
            db.SaveChanges();
        }, "FOREIGN KEY constraint failed");
    }
}
