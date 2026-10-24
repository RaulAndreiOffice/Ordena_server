using System.Globalization;
using Microsoft.Data.Sqlite;
using Ordena.Backend.Files;

namespace Ordena.Backend.Infrastructure;

// Copies the live database with SQLite's online backup, which also includes what is still in the WAL file.
internal sealed class SqliteBackup(IAppPaths paths)
{
    // Inside the archive, so the backups travel with the PDFs to a new PC.
    public const string ArchiveFolderName = "_backup";

    // Next to the database, for when the archive isn't available.
    public const string LocalFolderName = "backup";

    public const int Keep = 10;

    // Antivirus, the search indexer or a sync tool (OneDrive) may hold a file for a moment: the new copy, or
    // one already under its final name. Neither SQLite's backup nor the rename waits for that, so both are
    // tried again, each time a little later.
    private const int HeldAttempts = 6;
    private static readonly TimeSpan HeldDelay = TimeSpan.FromMilliseconds(100);

    private const string NameFormat = "yyyyMMdd-HHmm";

    // Writes <folder>/ordena-yyyyMMdd-HHmm.db and deletes the oldest backups beyond 10.
    // localTime names the file, so the name matches the clock on the wall. Returns the new file.
    public string BackupTo(string folder, DateTime localTime)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"ordena-{localTime.ToString(NameFormat, CultureInfo.InvariantCulture)}.db");
        var tmpPath = path + ".tmp";

        WhileHeld(() => CopyTo(tmpPath));

        // A half-written backup never has the final name, so a restore can't pick it up.
        WhileHeld(() => File.Move(tmpPath, path, overwrite: true));

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

    private static void WhileHeld(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (IsHeld(ex) && attempt < HeldAttempts)
            {
                Thread.Sleep(HeldDelay * attempt);
            }
        }
    }

    // SQLite says busy, locked or can't open; Windows says the file is in use, or, for a rename over a file
    // another program has open, that access is denied.
    private static bool IsHeld(Exception ex) => ex switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode is SqliteBusy or SqliteLocked or SqliteCantOpen,
        IOException io => FileStorage.IsLocked(io),
        UnauthorizedAccessException => true,
        _ => false,
    };

    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteCantOpen = 14;

    // Without pooling, so the files are closed as soon as the work is done.
    private static SqliteConnection Open(string path, bool readOnly = false) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
}
