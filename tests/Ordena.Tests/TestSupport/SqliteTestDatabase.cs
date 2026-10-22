using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Data;
using Ordena.Backend.Repositories;

namespace Ordena.Tests.TestSupport;

// A fresh SQLite database in memory, built by the real migrations.
// It lives as long as the connection stays open, so every test gets an empty one.
// It is also the DbContext factory, so tests use the real UnitOfWorkFactory on top of it.
internal sealed class SqliteTestDatabase : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteTestDatabase()
    {
        Connection = new SqliteConnection("Data Source=:memory:");
        Connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options;

        using var db = CreateContext();
        db.Database.Migrate();
    }

    public SqliteConnection Connection { get; }

    // A new context each time, so EF's identity cache can't hide what is really stored.
    public AppDbContext CreateContext() => new(_options);

    AppDbContext IDbContextFactory<AppDbContext>.CreateDbContext() => CreateContext();

    public IUnitOfWorkFactory UnitOfWorkFactory => new UnitOfWorkFactory(this);

    public T Save<T>(T entity) where T : class
    {
        using var db = CreateContext();
        db.Add(entity);
        db.SaveChanges();
        return entity;
    }

    public object? Scalar(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> read)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var rows = new List<T>();
        while (reader.Read())
            rows.Add(read(reader));
        return rows;
    }

    public void Dispose() => Connection.Dispose();
}
