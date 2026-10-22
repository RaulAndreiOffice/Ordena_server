using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Repositories;

public sealed class PartnerRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Suggest_puts_names_starting_with_the_text_first_and_respects_max()
    {
        _db.Save(TestData.NewPartner("Gas-Natural", "Gas Natural"));
        _db.Save(TestData.NewPartner("Naturgy", "Naturgy"));
        _db.Save(TestData.NewPartner("Natur-House", "Natur House"));
        _db.Save(TestData.NewPartner("Agua-Natural", "Agua Natural"));
        _db.Save(TestData.NewPartner("Repsol", "Repsol"));

        await using var uow = _db.UnitOfWorkFactory.Create();
        var suggestions = await uow.Partners.SuggestAsync("natur", 3, Ct);

        Assert.Equal(["Natur House", "Naturgy", "Agua Natural"], suggestions.Select(p => p.Name));
    }

    [Fact]
    public async Task GetByNameNorm_sees_a_partner_added_but_not_saved()
    {
        await using var uow = _db.UnitOfWorkFactory.Create();
        var added = TestData.NewPartner();
        uow.Partners.Add(added);

        Assert.Same(added, await uow.Partners.GetByNameNormAsync("telefonica espana", Ct));
    }

    [Fact]
    public async Task GetByNameNorm_finds_a_saved_partner()
    {
        _db.Save(TestData.NewPartner());

        await using var uow = _db.UnitOfWorkFactory.Create();

        Assert.NotNull(await uow.Partners.GetByNameNormAsync("telefonica espana", Ct));
        Assert.Null(await uow.Partners.GetByNameNormAsync("telefonica", Ct));
    }

    [Theory]
    [InlineData("Repsol")]
    [InlineData("REPSOL")]
    public async Task FolderNameExists_ignores_case_in_the_database(string folderName)
    {
        _db.Save(TestData.NewPartner("repsol", "Repsol"));

        await using var uow = _db.UnitOfWorkFactory.Create();

        Assert.True(await uow.Partners.FolderNameExistsAsync(folderName, Ct));
        Assert.False(await uow.Partners.FolderNameExistsAsync("Repsol-2", Ct));
    }

    [Fact]
    public async Task FolderNameExists_ignores_case_for_unsaved_partners()
    {
        await using var uow = _db.UnitOfWorkFactory.Create();
        uow.Partners.Add(TestData.NewPartner("Repsol", "Repsol"));

        Assert.True(await uow.Partners.FolderNameExistsAsync("REPSOL", Ct));
    }
}
