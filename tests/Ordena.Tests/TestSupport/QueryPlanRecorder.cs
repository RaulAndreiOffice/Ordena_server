using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ordena.Backend.Data;

namespace Ordena.Tests.TestSupport;

// SQLite's plan for every query EF reads with, as "SCAN d USING INDEX … / SEARCH p …": whether a query is
// served by an index or sorts every row first. Also a context factory, for a UnitOfWorkFactory over the
// test database with the recorder attached.
internal sealed class QueryPlanRecorder(SqliteTestDatabase db) : DbCommandInterceptor, IDbContextFactory<AppDbContext>
{
    public List<(string Sql, string Plan)> Queries { get; } = [];

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(db.Connection).AddInterceptors(this).Options);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        using var explain = command.Connection!.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        foreach (DbParameter parameter in command.Parameters)
        {
            var copy = explain.CreateParameter();
            copy.ParameterName = parameter.ParameterName;
            copy.Value = parameter.Value;
            explain.Parameters.Add(copy);
        }

        List<string> steps = [];
        using (var reader = explain.ExecuteReader())
        {
            while (reader.Read())
                steps.Add(reader.GetString(3));
        }
        Queries.Add((command.CommandText, string.Join(" / ", steps)));
    }
}
