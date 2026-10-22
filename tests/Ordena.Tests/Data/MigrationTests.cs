using Microsoft.EntityFrameworkCore;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Data;

public sealed class MigrationTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Migrations_cover_the_current_model()
    {
        using var db = _db.CreateContext();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model changed without a migration. Run: dotnet ef migrations add <Name> --project src/Ordena.Backend --output-dir Data/Migrations");
    }

    [Fact]
    public void Migrate_applies_InitialCreate()
    {
        using var db = _db.CreateContext();

        Assert.Contains(db.Database.GetAppliedMigrations(), m => m.EndsWith("_InitialCreate"));
        Assert.Empty(db.Database.GetPendingMigrations());
    }
}
