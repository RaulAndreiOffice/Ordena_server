using Microsoft.Extensions.Logging.Abstractions;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

// "Añadir a Ordena" for the PDFs "Comprobar carpeta" found in the archive with no document.
public sealed class FileRegistrationTests : IDisposable
{
    private const string Folder = "2026/1T/Recibidas/Telefonica-Espana/";

    private readonly SqliteTestDatabase _db = new();
    private readonly TempFolder _archive = new();
    private readonly TempFolder _desktop = new();
    private readonly FileRegistration _registration;
    private readonly Partner _partner;

    public FileRegistrationTests()
    {
        _registration = new FileRegistration(_db.UnitOfWorkFactory, new FileStorage(TimeSpan.FromMilliseconds(1)),
            new FixedClock(TestData.Now), NullLogger<FileRegistration>.Instance);
        _partner = _db.Save(TestData.NewPartner());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _archive.Dispose();
        _desktop.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task A_file_named_like_the_app_does_is_registered_where_it_is()
    {
        const string relativePath = Folder + "2026-03-14_Telefonica-Espana_A-2026-0123_Ticket.pdf";
        var path = Dropped(relativePath);

        var report = await _registration.RunAsync(_archive.Root, [path], Ct);

        Assert.Equal(1, report.Registered);
        Assert.Empty(report.Remaining);
        Assert.Equal([relativePath], _archive.Files()); // not moved, not renamed
        Assert.Equal(1L, _db.Scalar("SELECT IsPaid FROM Documents")); // already archived: settled
        Assert.Equal(
            [$"{relativePath} | {_partner.Id} | A-2026-0123 | A20260123 | 2026-03-14 | {(int)DocType.FacturaSimplificada} | "
             + $"{(int)Direction.Recibida} | {Sha256Hasher.ComputeFile(path)} | {new FileInfo(path).Length} | active"],
            Rows());
    }

    [Fact]
    public async Task A_partner_folder_the_database_doesnt_know_becomes_one_new_partner()
    {
        const string folder = "2026/1T/Emitidas/Cliente-Ejemplo-SL/";
        var first = Dropped(folder + "2026-02-01_Cliente-Ejemplo-SL_F-7.pdf");
        var second = Dropped(folder + "2026-02-03_Cliente-Ejemplo-SL_F-8_Proforma.pdf");

        var report = await _registration.RunAsync(_archive.Root, [first, second], Ct);

        Assert.Equal(2, report.Registered);
        var partners = _db.Query("SELECT Id, Name, FolderName FROM Partners ORDER BY Id",
            r => (r.GetInt32(0), r.GetString(1), r.GetString(2)));
        Assert.Equal((_partner.Id + 1, "Cliente Ejemplo SL", "Cliente-Ejemplo-SL"), partners[^1]);
        Assert.Equal(2, partners.Count);
        Assert.All(_db.Query("SELECT PartnerId FROM Documents", r => r.GetInt32(0)), id => Assert.Equal(_partner.Id + 1, id));
    }

    [Fact]
    public async Task A_file_in_the_trash_folder_is_registered_as_deleted()
    {
        var path = Dropped("_Papelera/" + Folder + "2026-03-14_Telefonica-Espana_R-1_Recibo.pdf");

        await _registration.RunAsync(_archive.Root, [path], Ct);

        Assert.EndsWith("| deleted", Assert.Single(Rows()));
    }

    [Fact]
    public async Task What_the_name_cant_tell_or_the_database_already_has_is_left_for_the_manual_queue()
    {
        var archived = Dropped(Folder + "2026-03-14_Telefonica-Espana_A-1.pdf");
        _db.Save(TestData.NewDocument(_partner.Id, sha256: Sha256Hasher.ComputeFile(archived), relativePath: "elsewhere.pdf"));
        _db.Save(TestData.NewDocument(_partner.Id, docNumber: "A-9"));

        string[] paths =
        [
            Dropped("2026/escaneo suelto.pdf"), // not the app's naming
            Dropped(Folder + "2026-03-14_Telefonica-Espana.pdf"), // no number
            archived, // its content is already archived under another name
            Dropped(Folder + "2026-03-14_Telefonica-Espana_A-9.pdf"), // the partner already has that number
            Path.GetFullPath(_desktop.WriteFile(Folder + "2026-03-14_Telefonica-Espana_A-10.pdf", TestPdf.Bytes("desktop"))),
            Dropped(Folder + "2026-03-14_Telefonica-Espana_B-1.pdf"), // registered
            Dropped(Folder + "2026-03-14_Telefonica-Espana_B-1_2.pdf"), // the same number again in this batch
        ];

        var report = await _registration.RunAsync(_archive.Root, paths, Ct);

        Assert.Equal(1, report.Registered);
        Assert.Equal(paths.Where((_, i) => i != 5), report.Remaining);
        Assert.Contains(Rows(), r => r.StartsWith(Folder + "2026-03-14_Telefonica-Espana_B-1.pdf |", StringComparison.Ordinal));
    }

    // A PDF copied into the archive by hand, with content unique to its path. Returns the full path.
    private string Dropped(string relativePath) =>
        Path.GetFullPath(_archive.WriteFile(relativePath, TestPdf.Bytes(relativePath)));

    private List<string> Rows() => _db.Query("""
        SELECT RelativePath, PartnerId, DocNumber, DocNumberNorm, IssueDate, Type, Direction, Sha256, FileSizeBytes,
               CASE WHEN DeletedAt IS NULL THEN 'active' ELSE 'deleted' END
        FROM Documents WHERE RelativePath LIKE '2026/%' OR RelativePath LIKE '_Papelera/%' ORDER BY RelativePath
        """, r => string.Join(" | ", Enumerable.Range(0, r.FieldCount).Select(i => r.GetValue(i).ToString())));
}
