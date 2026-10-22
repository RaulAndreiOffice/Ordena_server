using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ordena.Backend.Data;

// Runs on every connection EF opens. These are the settings measured in the benchmark (Plan.md):
// WAL + synchronous=NORMAL for fast commits (the journal handles recovery),
// a 20 MB page cache so the whole database stays in memory, temporary tables in RAM.
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    // Only these three on a read-only connection: switching a database to WAL writes to the file.
    // EF opens one read-only to check that the database exists before migrating.
    private const string ConnectionPragmas = "PRAGMA synchronous=NORMAL; PRAGMA cache_size=-20000; PRAGMA temp_store=MEMORY;";
    private const string WalPragma = "PRAGMA journal_mode=WAL; ";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection);

    // Microsoft.Data.Sqlite runs async calls synchronously anyway.
    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    private static void Apply(DbConnection connection)
    {
        var readOnly = new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.ReadOnly;

        using var command = connection.CreateCommand();
        command.CommandText = readOnly ? ConnectionPragmas : WalPragma + ConnectionPragmas;
        command.ExecuteNonQuery();
    }
}
