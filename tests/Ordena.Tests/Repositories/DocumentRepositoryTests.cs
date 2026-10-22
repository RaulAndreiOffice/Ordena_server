using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Repositories;

public sealed class DocumentRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetById_includes_the_partner()
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = _db.Save(TestData.NewDocument(partner.Id));

        await using var uow = _db.UnitOfWorkFactory.Create();
        var loaded = await uow.Documents.GetByIdAsync(document.Id, Ct);

        Assert.Equal("Telefónica España, S.A.U.", loaded?.Partner.Name);
    }

    [Fact]
    public async Task GetByHash_finds_deleted_documents_too()
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = TestData.NewDocument(partner.Id, sha256: new string('d', 64));
        document.DeletedAt = TestData.Now;
        _db.Save(document);

        await using var uow = _db.UnitOfWorkFactory.Create();

        Assert.NotNull(await uow.Documents.GetByHashAsync(new string('d', 64), Ct));
        Assert.Null(await uow.Documents.GetByHashAsync(new string('e', 64), Ct));
    }

    [Fact]
    public async Task NumberExists_matches_the_same_partner_and_number_only()
    {
        var telefonica = _db.Save(TestData.NewPartner());
        var repsol = _db.Save(TestData.NewPartner("Repsol", "Repsol S.A."));
        var document = _db.Save(TestData.NewDocument(telefonica.Id, docNumber: "A-2026/0123"));

        await using var uow = _db.UnitOfWorkFactory.Create();

        Assert.True(await uow.Documents.NumberExistsAsync(telefonica.Id, "A20260123", null, Ct));
        Assert.False(await uow.Documents.NumberExistsAsync(repsol.Id, "A20260123", null, Ct));
        Assert.False(await uow.Documents.NumberExistsAsync(telefonica.Id, "A20260124", null, Ct));
        // While editing a document, its own number is not a duplicate.
        Assert.False(await uow.Documents.NumberExistsAsync(telefonica.Id, "A20260123", document.Id, Ct));
    }

    [Fact]
    public async Task NumberExists_ignores_deleted_documents()
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = TestData.NewDocument(partner.Id, docNumber: "A-2026/0123");
        document.DeletedAt = TestData.Now;
        _db.Save(document);

        await using var uow = _db.UnitOfWorkFactory.Create();

        Assert.False(await uow.Documents.NumberExistsAsync(partner.Id, "A20260123", null, Ct));
    }

    [Fact]
    public async Task RelativePathExists_ignores_case_and_sees_deleted_documents()
    {
        var partner = _db.Save(TestData.NewPartner());
        _db.Save(TestData.NewDocument(partner.Id, relativePath: "2026/1T/Recibidas/Repsol/a.pdf"));
        var deleted = TestData.NewDocument(partner.Id, relativePath: "_Papelera/b.pdf");
        deleted.DeletedAt = TestData.Now;
        _db.Save(deleted);

        await using var uow = _db.UnitOfWorkFactory.Create();

        Assert.True(await uow.Documents.RelativePathExistsAsync("2026/1t/recibidas/REPSOL/A.PDF", Ct));
        Assert.True(await uow.Documents.RelativePathExistsAsync("_Papelera/b.pdf", Ct));
        Assert.False(await uow.Documents.RelativePathExistsAsync("2026/1T/Recibidas/Repsol/c.pdf", Ct));
    }

    [Fact]
    public async Task ListActive_skips_deleted_documents()
    {
        var partner = _db.Save(TestData.NewPartner());
        var active = _db.Save(TestData.NewDocument(partner.Id));
        var deleted = TestData.NewDocument(partner.Id);
        deleted.DeletedAt = TestData.Now;
        _db.Save(deleted);

        await using var uow = _db.UnitOfWorkFactory.Create();
        var documents = await uow.Documents.ListActiveAsync(Ct);

        Assert.Equal(active.Id, Assert.Single(documents).Id);
    }

    [Fact]
    public async Task Add_is_saved_by_the_unit_of_work()
    {
        var partner = _db.Save(TestData.NewPartner());

        await using (var uow = _db.UnitOfWorkFactory.Create())
        {
            uow.Documents.Add(TestData.NewDocument(partner.Id));
            await uow.SaveChangesAsync(Ct);
        }

        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM Documents"));
    }
}
