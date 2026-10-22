using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Ordena.Backend.Infrastructure;

// Copies the live database with SQLite's online backup, which also includes what is still in the WAL file.
internal sealed class SqliteBackup(IAppPaths paths)
{
    // Inside the archive, so the backups travel with the PDFs to a new PC.
    public const string ArchiveFolderName = "_backup";

    // Next to the database, for when the archive isn't available.
    public const string LocalFolderName = "backup";

    public const int Keep = 10;

    // Writes <folder>/ordena-yyyyMMdd-HHmm.db and deletes the oldest backups beyond 10.
    // localTime names the file, so the name matches the clock on the wall. Returns the new file.
    public string BackupTo(string folder, DateTime localTime)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"ordena-{localTime:yyyyMMdd-HHmm}.db"));
        var tmpPath = path + ".tmp";

        using (var source = Open(paths.Database))
        using (var target = Open(tmpPath))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);

            // One self-contained file, with no -wal or -shm beside it, that opens read-only anywhere.
            using var command = target.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=DELETE";
            command.ExecuteNonQuery();
        }

        // A half-written backup never has the final name, so a restore can't pick it up.
        File.Move(tmpPath, path, overwrite: true);

        // The names sort by date, oldest first.
        var backups = Directory.GetFiles(folder, "ordena-*.db").Order(StringComparer.Ordinal).ToList();
        foreach (var old in backups.Take(backups.Count - Keep))
            File.Delete(old);

        return path;
    }

    // The newest backup in the folder that opens and passes SQLite's own check; a damaged one is skipped.
    public string? FindNewestUsable(string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        return Directory.GetFiles(folder, "ordena-*.db")
            .OrderDescending(StringComparer.Ordinal)
            .FirstOrDefault(IsUsable);
    }

    // Replaces what is in the live database with the backup's content. SQLite's backup API writes it
    // like any other change, so the database's own WAL files stay valid. Idle pooled connections are
    // closed before and after, so none of them works on the old content.
    public void RestoreFrom(string backupFile)
    {
        SqliteConnection.ClearAllPools();
        using (var source = Open(backupFile, readOnly: true))
        using (var target = Open(paths.Database))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }
        SqliteConnection.ClearAllPools();
    }

    private static bool IsUsable(string file)
    {
        try
        {
            using var connection = Open(file, readOnly: true);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check";
            if (command.ExecuteScalar() as string != "ok")
                return false;

            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Documents'";
            return (long)command.ExecuteScalar()! == 1;
        }
        catch (SqliteException)
        {
            return false; // not a database at all
        }
    }

    // Without pooling, so the files are closed as soon as the work is done.
    private static SqliteConnection Open(string path, bool readOnly = false) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
}
