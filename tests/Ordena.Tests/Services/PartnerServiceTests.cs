using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

public sealed class PartnerServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly PartnerService _partners;

    public PartnerServiceTests() => _partners = new PartnerService(_db.UnitOfWorkFactory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("telef")]
    [InlineData("TELEFÓ")]
    [InlineData("España")]
    public async Task Suggest_normalizes_what_is_typed(string text)
    {
        _db.Save(TestData.NewPartner());
        _db.Save(TestData.NewPartner("Repsol", "Repsol S.A."));

        var suggestions = await _partners.SuggestAsync(text, 10, Ct);

        Assert.Equal("Telefónica España, S.A.U.", Assert.Single(suggestions).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("--")]
    public async Task Suggest_returns_nothing_for_empty_text(string text)
    {
        _db.Save(TestData.NewPartner());

        Assert.Empty(await _partners.SuggestAsync(text, 10, Ct));
    }

    [Theory]
    [InlineData("Telefonica Espana SA")]
    [InlineData("TELEFÓNICA ESPAÑA, S.L.")]
    [InlineData("telefonica-espana")]
    public async Task FindSimilar_matches_the_same_name_after_normalization(string name)
    {
        _db.Save(TestData.NewPartner());

        var similar = await _partners.FindSimilarAsync(name, Ct);

        Assert.Equal("Telefónica España, S.A.U.", similar?.Name);
    }

    [Fact]
    public async Task FindSimilar_returns_null_for_a_different_name()
    {
        _db.Save(TestData.NewPartner());

        Assert.Null(await _partners.FindSimilarAsync("Telefónica Móviles", Ct));
    }
}
