using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

public sealed class PartnerResolverTests : IDisposable
{
    private const string Valencia = "Asociación de Comerciantes del Centro Histórico de Valencia";
    private const string Sevilla = "Asociación de Comerciantes del Centro Histórico de Sevilla";

    private static readonly DateTime CreatedAt = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _db = new();
    private readonly PartnerResolver _resolver = new(new FixedClock(CreatedAt));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Reuses_the_partner_with_the_same_normalized_name()
    {
        var existing = _db.Save(TestData.NewPartner());

        await using (var uow = _db.UnitOfWorkFactory.Create())
        {
            var result = await _resolver.GetOrCreateAsync(uow, "TELEFONICA ESPAÑA SL", Ct);
            await uow.SaveChangesAsync(Ct);

            Assert.Equal(existing.Id, result.Value.Id);
        }

        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM Partners"));
    }

    [Fact]
    public async Task Creates_a_partner_with_its_folder_name_and_creation_time()
    {
        await using (var uow = _db.UnitOfWorkFactory.Create())
        {
            var result = await _resolver.GetOrCreateAsync(uow, "  Repsol S.A. ", Ct);
            await uow.SaveChangesAsync(Ct);

            Assert.True(result.IsSuccess);
        }

        using var db = _db.CreateContext();
        var partner = await db.Partners.SingleAsync(Ct);
        Assert.Equal("Repsol S.A.", partner.Name);
        Assert.Equal("repsol", partner.NameNorm);
        Assert.Equal("Repsol", partner.FolderName);
        Assert.Equal(CreatedAt, partner.CreatedAt);
    }

    [Fact]
    public async Task Adds_a_number_when_the_folder_name_is_taken()
    {
        // Both names give the same first 40 characters: "Asociacion-de-Comerciantes-del-Centro-Hi".
        await Resolve(Valencia);
        await Resolve(Sevilla);

        Assert.Equal(
            ["Asociacion-de-Comerciantes-del-Centro-2", "Asociacion-de-Comerciantes-del-Centro-Hi"],
            FolderNames());
    }

    [Fact]
    public async Task Two_new_partners_in_one_unit_of_work_get_different_folders()
    {
        // Bulk import creates many partners before saving once.
        await using (var uow = _db.UnitOfWorkFactory.Create())
        {
            await _resolver.GetOrCreateAsync(uow, Valencia, Ct);
            await _resolver.GetOrCreateAsync(uow, Sevilla, Ct);
            await uow.SaveChangesAsync(Ct);
        }

        Assert.Equal(2, FolderNames().Distinct().Count());
    }

    [Fact]
    public async Task The_same_new_name_twice_in_one_unit_of_work_is_one_partner()
    {
        await using var uow = _db.UnitOfWorkFactory.Create();

        var first = await _resolver.GetOrCreateAsync(uow, "Repsol S.A.", Ct);
        var second = await _resolver.GetOrCreateAsync(uow, "REPSOL", Ct);

        Assert.Same(first.Value, second.Value);
    }

    [Theory]
    [InlineData("S.L.")]
    [InlineData("---")]
    [InlineData("   ")]
    public async Task A_name_with_nothing_left_after_normalization_fails(string name)
    {
        await using var uow = _db.UnitOfWorkFactory.Create();

        var result = await _resolver.GetOrCreateAsync(uow, name, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidPartnerName, result.Error);
    }

    private async Task Resolve(string name)
    {
        await using var uow = _db.UnitOfWorkFactory.Create();
        await _resolver.GetOrCreateAsync(uow, name, Ct);
        await uow.SaveChangesAsync(Ct);
    }

    private string[] FolderNames() =>
        _db.Query("SELECT FolderName FROM Partners ORDER BY FolderName", r => r.GetString(0)).ToArray();
}
