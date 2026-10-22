using Microsoft.EntityFrameworkCore;

namespace Ordena.Backend.Data;

// Creates the database, or upgrades it to the latest migration. Never EnsureCreated.
internal sealed class DatabaseMigrator(IDbContextFactory<AppDbContext> contextFactory)
{
    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await db.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    // True after an app update that changes the schema: the database gets backed up first.
    public async Task<bool> HasPendingMigrationsAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return (await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).Any();
    }
}
