using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Repositories;

public sealed class SettingsRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Set_inserts_a_new_key_then_updates_it()
    {
        await Set("Language", "es");
        await Set("Language", "en");

        await using var uow = _db.UnitOfWorkFactory.Create();
        var all = await uow.Settings.GetAllAsync(Ct);

        Assert.Equal("en", Assert.Single(all, kv => kv.Key == "Language").Value);
        Assert.Single(all);
    }

    private async Task Set(string key, string value)
    {
        await using var uow = _db.UnitOfWorkFactory.Create();
        await uow.Settings.SetAsync(key, value, Ct);
        await uow.SaveChangesAsync(Ct);
    }
}
