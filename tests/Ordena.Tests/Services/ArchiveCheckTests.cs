using Microsoft.Extensions.Logging.Abstractions;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

// "Comprobar carpeta" after the user moved, renamed, deleted or added files by hand in Explorer.
public sealed class ArchiveCheckTests : IDisposable
{
    private const string Folder = "2026/1T/Recibidas/Telefonica-Espana/";

    private readonly SqliteTestDatabase _db = new();
    private readonly TempFolder _archive = new();
    private readonly ArchiveCheck _check;
    private readonly int _partnerId;

    public ArchiveCheckTests()
    {
        _check = new ArchiveCheck(
            _db.UnitOfWorkFactory, new FileStorage(TimeSpan.FromMilliseconds(1)), NullLogger<ArchiveCheck>.Instance);
        _partnerId = _db.Save(TestData.NewPartner()).Id;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _archive.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task An_archive_that_matches_the_database_reports_nothing()
    {
        Archived(Folder + "a.pdf");
        Archived(Folder + "b.pdf");

        var report = await _check.RunAsync(_archive.Root, Ct);

        Assert.Equal(2, report.DocumentCount);
        Assert.Empty(report.Relinked);
        Assert.Empty(report.Missing);
        Assert.Empty(report.UnknownFiles);
    }

    [Fact]
    public async Task A_file_moved_to_another_folder_is_moved_back()
    {
        var document = Archived(Folder + "a.pdf");
        MoveByHand(Folder + "a.pdf", "2026/Otros/a.pdf");

        var report = await _check.RunAsync(_archive.Root, Ct);

        Assert.Equal([new RelinkedFile(document.Id, "2026/Otros/a.pdf", Folder + "a.pdf")], report.Relinked);
        Assert.Equal([Folder + "a.pdf"], _archive.Files());
        Assert.False(Directory.Exists(_archive.Path("2026", "Otros"))); // left empty, so removed
    }

    [Fact]
    public async Task A_renamed_file_is_found_by_its_content()
    {
        var document = Archived(Folder + "a.pdf");
        MoveByHand(Folder + "a.pdf", Folder + "renamed by hand.pdf");

        var report = await _check.RunAsync(_archive.Root, Ct);

        Assert.Equal(document.Id, Assert.Single(report.Relinked).DocumentId);
        Assert.Equal([Folder + "a.pdf"], _archive.Files());
    }

    [Fact]
    public async Task A_file_with_the_same_name_but_other_content_is_not_taken()
    {
        var document = Archived(Folder + "a.pdf", onDisk: false);
        _archive.WriteFile("2026/Otros/a.pdf", TestPdf.Bytes("another invoice"));

        var report = await _check.RunAsync(_archive.Root, Ct);

        Assert.Empty(report.Relinked);
        Assert.Equal(document.Id, Assert.Single(report.Missing).Id);
        Assert.Equal([_archive.Path("2026", "Otros", "a.pdf")], report.UnknownFiles);
    }

    [Fact]
    public async Task A_file_that_is_nowhere_is_reported_missing()
    {
        var document = Archived(Folder + "a.pdf", onDisk: false);

        var report = await _check.RunAsync(_archive.Root, Ct);

        var missing = Assert.Single(report.Missing);
        Assert.Equal((document.Id, "Telefónica España, S.A.U.", Folder + "a.pdf"),
            (missing.Id, missing.PartnerName, missing.RelativePath));
    }

    [Fact]
    public async Task Unknown_pdfs_are_listed_and_other_files_ignored()
    {
        Archived(Folder + "a.pdf");
        _archive.WriteFile("2026/escaneo.pdf", TestPdf.Bytes("dropped in by hand"));
        _archive.WriteFile("_backup/ordena-20261001-0800.db", [1]);
        _archive.WriteFile("desktop.ini", [1]);
        _archive.WriteFile(Folder + "b.pdf.tmp", [1]);

        var report = await _check.RunAsync(_archive.Root, Ct);

        Assert.Equal([_archive.Path("2026", "escaneo.pdf")], report.UnknownFiles);
    }

    [Fact]
    public async Task A_deleted_documents_file_goes_back_to_the_trash()
    {
        var document = Archived("_Papelera/" + Folder + "a.pdf", onDisk: false, deleted: true);
        _archive.WriteFile("2026/a.pdf", TestPdf.Bytes("_Papelera/" + Folder + "a.pdf"));

        var report = await _check.RunAsync(_archive.Root, Ct);

        Assert.Equal(document.Id, Assert.Single(report.Relinked).DocumentId);
        Assert.Equal(["_Papelera/" + Folder + "a.pdf"], _archive.Files());
    }

    // A saved document; its file holds content unique to its path, so its hash is known.
    private Document Archived(string relativePath, bool onDisk = true, bool deleted = false)
    {
        var content = TestPdf.Bytes(relativePath);
        var document = TestData.NewDocument(_partnerId, sha256: Sha256Hasher.Compute(content), relativePath: relativePath);
        if (deleted)
            document.DeletedAt = TestData.Now;
        _db.Save(document);
        if (onDisk)
            _archive.WriteFile(relativePath, content);
        return document;
    }

    private void MoveByHand(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_archive.Path(to))!);
        File.Move(_archive.Path(from), _archive.Path(to));
    }
}
