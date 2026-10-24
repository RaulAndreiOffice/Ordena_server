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

    // Antivirus, the search indexer or a sync tool (OneDrive) may hold the new file for a moment, and
    // SQLite's backup doesn't wait for a lock: it reports the database as busy. Each try copies again.
    private const int BusyAttempts = 6;
    private static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(100);

    private const string NameFormat = "yyyyMMdd-HHmm";

    // Writes <folder>/ordena-yyyyMMdd-HHmm.db and deletes the oldest backups beyond 10.
    // localTime names the file, so the name matches the clock on the wall. Returns the new file.
    public string BackupTo(string folder, DateTime localTime)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"ordena-{localTime.ToString(NameFormat, CultureInfo.InvariantCulture)}.db");
        var tmpPath = path + ".tmp";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                CopyTo(tmpPath);
                break;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteBusy or SqliteLocked && attempt < BusyAttempts)
            {
                Thread.Sleep(BusyDelay * attempt);
            }
        }

        // A half-written backup never has the final name, so a restore can't pick it up.
        File.Move(tmpPath, path, overwrite: true);

        // The names sort by date, oldest first.
        var backups = Directory.GetFiles(folder, "ordena-*.db").Order(StringComparer.Ordinal).ToList();
        foreach (var old in backups.Take(backups.Count - Keep))
            File.Delete(old);

        return path;
    }

    private void CopyTo(string targetPath)
    {
        using var source = Open(paths.Database);
        using var target = Open(targetPath);
        source.Open();
        target.Open();
        source.BackupDatabase(target);

        // One self-contained file, with no -wal or -shm beside it, that opens read-only anywhere.
        using var command = target.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE";
        command.ExecuteNonQuery();
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

    // When a backup was made, local time, from its name; a file renamed by hand gives its write time.
    public static DateTime TimeOf(string backupFile) =>
        DateTime.TryParseExact(Path.GetFileNameWithoutExtension(backupFile).Replace("ordena-", "", StringComparison.Ordinal),
            NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : File.GetLastWriteTime(backupFile);

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

    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    // Without pooling, so the files are closed as soon as the work is done.
    private static SqliteConnection Open(string path, bool readOnly = false) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
}
