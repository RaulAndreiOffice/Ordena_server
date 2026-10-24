using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Data;

// Pins the schema from the FigJam board ("5. Database") by reading SQLite's own catalog.
public sealed class SchemaTests : IDisposable
{
    private static readonly Dictionary<string, string[]> Columns = new()
    {
        ["Partners"] = ["Id", "Name", "NameNorm", "FolderName", "CreatedAt"],
        ["Documents"] =
        [
            "Id", "Sha256", "PartnerId", "DocNumber", "DocNumberNorm", "IssueDate", "Type", "Direction",
            "RelativePath", "OriginalName", "FileSizeBytes", "ImportedAt", "UpdatedAt", "DeletedAt", "IsPaid",
        ],
        ["FileOperations"] = ["Id", "DocumentId", "FromPath", "ToPath", "State", "Error", "CreatedAt"],
        ["Settings"] = ["Key", "Value"],
    };

    private static readonly string[] NullableColumns = ["Documents.DeletedAt", "FileOperations.Error"];

    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("Partners")]
    [InlineData("Documents")]
    [InlineData("FileOperations")]
    [InlineData("Settings")]
    public void Tables_have_the_board_columns_and_nullability(string table)
    {
        var columns = _db.Query(
            $"SELECT name, \"notnull\" FROM pragma_table_info('{table}')",
            r => (Name: r.GetString(0), NotNull: r.GetInt64(1) == 1));

        Assert.Equal(Columns[table].Order(), columns.Select(c => c.Name).Order());
        Assert.All(columns, c =>
            Assert.True(c.NotNull != NullableColumns.Contains($"{table}.{c.Name}"),
                $"{table}.{c.Name} has the wrong nullability"));
    }

    [Theory]
    [InlineData("Partners", new[] { "FolderName (unique)", "NameNorm" })]
    [InlineData("Documents", new[] { "DocNumberNorm", "IsPaid+IssueDate+Id", "IssueDate", "PartnerId+DocNumberNorm", "RelativePath (unique)", "Sha256 (unique)" })]
    [InlineData("FileOperations", new[] { "DocumentId" })]
    [InlineData("Settings", new string[0])]
    public void Indexes_match_the_board(string table, string[] expected)
    {
        // origin 'c' = created by CREATE INDEX; this skips the automatic primary-key index.
        var indexes = _db.Query(
            $"SELECT name, \"unique\" FROM pragma_index_list('{table}') WHERE origin = 'c'",
            r => (Name: r.GetString(0), Unique: r.GetInt64(1) == 1));

        var actual = indexes.Select(index =>
        {
            var columns = _db.Query(
                $"SELECT name FROM pragma_index_info('{index.Name}') ORDER BY seqno",
                r => r.GetString(0));
            return string.Join("+", columns) + (index.Unique ? " (unique)" : "");
        });

        Assert.Equal(expected.Order(), actual.Order());
    }

    [Theory]
    [InlineData("Documents", "PartnerId -> Partners.Id ON DELETE RESTRICT")]
    [InlineData("FileOperations", "DocumentId -> Documents.Id ON DELETE RESTRICT")]
    public void Foreign_keys_restrict_delete(string table, string expected)
    {
        var foreignKeys = _db.Query(
            $"SELECT \"from\", \"table\", \"to\", on_delete FROM pragma_foreign_key_list('{table}')",
            r => $"{r.GetString(0)} -> {r.GetString(1)}.{r.GetString(2)} ON DELETE {r.GetString(3)}");

        Assert.Equal(expected, Assert.Single(foreignKeys));
    }
}
