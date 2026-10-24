using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Ordena.Backend.Data;
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

    // A database from before the payment status: its documents count as paid, so only new ones are pending.
    [Fact]
    public void AddPaymentStatus_marks_the_documents_already_archived_as_paid()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        migrator.Migrate(db.Database.GetMigrations().Single(m => m.EndsWith("_InitialCreate")));
        Execute(connection, """
            INSERT INTO Partners (Name, NameNorm, FolderName, CreatedAt) VALUES ('Repsol', 'repsol', 'Repsol', '2026-01-01 00:00:00');
            INSERT INTO Documents (Sha256, PartnerId, DocNumber, DocNumberNorm, IssueDate, Type, Direction, RelativePath,
                                   OriginalName, FileSizeBytes, ImportedAt, UpdatedAt)
            VALUES ('ab', 1, 'T-1', 'T1', '2026-03-20', 1, 1, 'a.pdf', 'a.pdf', 1, '2026-03-20 00:00:00', '2026-03-20 00:00:00');
            """);

        migrator.Migrate();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsPaid FROM Documents";
        Assert.Equal(1L, command.ExecuteScalar());
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
