using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ordena.Tests.TestSupport;

internal static class AssertDb
{
    private const int SqliteConstraint = 19;

    // Expects SQLite itself to reject the save, and checks the message names the right constraint,
    // e.g. "UNIQUE constraint failed: Partners.FolderName".
    public static void Rejects(Action save, string expectedMessage)
    {
        var ex = Assert.Throws<DbUpdateException>(save);
        var sqlite = Assert.IsType<SqliteException>(ex.InnerException);
        Assert.Equal(SqliteConstraint, sqlite.SqliteErrorCode);
        Assert.Contains(expectedMessage, sqlite.Message);
    }
}
