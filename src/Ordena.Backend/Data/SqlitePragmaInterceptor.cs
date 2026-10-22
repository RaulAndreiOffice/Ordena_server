using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ordena.Backend.Data;

// Runs on every connection EF opens. These are the settings measured in the benchmark (Plan.md):
// WAL + synchronous=NORMAL for fast commits (the journal handles recovery),
// a 20 MB page cache so the whole database stays in memory, temporary tables in RAM.
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas =
        "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-20000; PRAGMA temp_store=MEMORY;";

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
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }
}
