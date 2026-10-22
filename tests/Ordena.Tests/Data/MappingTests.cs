using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Models;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Data;

// Saves entities, then reads them back in a fresh context to prove what SQLite really stores.
public sealed class MappingTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Document_round_trips_every_column()
    {
        var partner = _db.Save(TestData.NewPartner());
        var saved = _db.Save(new Document
        {
            PartnerId = partner.Id,
            Sha256 = new string('a', 64),
            DocNumber = "A-2026/0123",
            DocNumberNorm = "A20260123",
            IssueDate = new DateOnly(2026, 3, 14),
            Type = DocType.FacturaRectificativa,
            Direction = Direction.Emitida,
            RelativePath = "2026/1T/Emitidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123_Rectif.pdf",
            OriginalName = "factura marzo.pdf",
            FileSizeBytes = 5_000_000_000, // over int.MaxValue
            ImportedAt = TestData.Now,
            UpdatedAt = TestData.Now.AddHours(1),
            DeletedAt = TestData.Now.AddDays(2),
        });

        using var db = _db.CreateContext();
        var loaded = db.Documents.Include(d => d.Partner).Single();

        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal(saved.Sha256, loaded.Sha256);
        Assert.Equal(partner.Id, loaded.PartnerId);
        Assert.Equal(saved.DocNumber, loaded.DocNumber);
        Assert.Equal(saved.DocNumberNorm, loaded.DocNumberNorm);
        Assert.Equal(saved.IssueDate, loaded.IssueDate);
        Assert.Equal(saved.Type, loaded.Type);
        Assert.Equal(saved.Direction, loaded.Direction);
        Assert.Equal(saved.RelativePath, loaded.RelativePath);
        Assert.Equal(saved.OriginalName, loaded.OriginalName);
        Assert.Equal(saved.FileSizeBytes, loaded.FileSizeBytes);
        Assert.Equal(saved.ImportedAt, loaded.ImportedAt);
        Assert.Equal(saved.UpdatedAt, loaded.UpdatedAt);
        Assert.Equal(saved.DeletedAt, loaded.DeletedAt);
        Assert.Equal("Telefónica España, S.A.U.", loaded.Partner.Name);
    }

    [Fact]
    public void Partner_round_trips_with_its_documents()
    {
        var partner = _db.Save(TestData.NewPartner());
        _db.Save(TestData.NewDocument(partner.Id));
        _db.Save(TestData.NewDocument(partner.Id));

        using var db = _db.CreateContext();
        var loaded = db.Partners.Include(p => p.Documents).Single();

        Assert.Equal(partner.Name, loaded.Name);
        Assert.Equal(partner.NameNorm, loaded.NameNorm);
        Assert.Equal(partner.FolderName, loaded.FolderName);
        Assert.Equal(partner.CreatedAt, loaded.CreatedAt);
        Assert.Equal(2, loaded.Documents.Count);
    }

    [Fact]
    public void Journal_row_round_trips_every_column()
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = _db.Save(TestData.NewDocument(partner.Id));
        var saved = TestData.NewFileOperation(document.Id);
        saved.State = FileOperationState.Failed;
        saved.Error = "El archivo está abierto en otro programa";
        _db.Save(saved);

        using var db = _db.CreateContext();
        var loaded = db.FileOperations.Single();

        Assert.Equal(document.Id, loaded.DocumentId);
        Assert.Equal(saved.FromPath, loaded.FromPath);
        Assert.Equal(saved.ToPath, loaded.ToPath);
        Assert.Equal(saved.State, loaded.State);
        Assert.Equal(saved.Error, loaded.Error);
        Assert.Equal(saved.CreatedAt, loaded.CreatedAt);
    }

    [Fact]
    public void Setting_round_trips()
    {
        _db.Save(new AppSetting { Key = "ArchiveRoot", Value = @"C:\Users\me\Documents\Facturas" });

        using var db = _db.CreateContext();

        Assert.Equal(@"C:\Users\me\Documents\Facturas", db.Settings.Find("ArchiveRoot")?.Value);
    }

    [Fact]
    public void Nullable_columns_round_trip_as_null()
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = _db.Save(TestData.NewDocument(partner.Id));
        _db.Save(TestData.NewFileOperation(document.Id));

        using var db = _db.CreateContext();

        Assert.Null(db.Documents.Single().DeletedAt);
        Assert.Null(db.FileOperations.Single().Error);
    }

    [Fact]
    public void Timestamps_come_back_as_utc()
    {
        // Assert.Equal on DateTime ignores Kind, so Kind is checked explicitly here.
        var partner = _db.Save(TestData.NewPartner());
        var document = TestData.NewDocument(partner.Id);
        document.DeletedAt = TestData.Now;
        _db.Save(document);
        _db.Save(TestData.NewFileOperation(document.Id));

        using var db = _db.CreateContext();
        var loadedPartner = db.Partners.Single();
        var loadedDocument = db.Documents.Single();
        var loadedOperation = db.FileOperations.Single();

        DateTime[] timestamps =
        [
            loadedPartner.CreatedAt,
            loadedDocument.ImportedAt,
            loadedDocument.UpdatedAt,
            loadedDocument.DeletedAt!.Value,
            loadedOperation.CreatedAt,
        ];
        Assert.All(timestamps, t =>
        {
            Assert.Equal(DateTimeKind.Utc, t.Kind);
            Assert.Equal(TestData.Now, t);
        });
    }

    [Fact]
    public void IssueDate_is_stored_as_iso_text()
    {
        var partner = _db.Save(TestData.NewPartner());
        _db.Save(TestData.NewDocument(partner.Id));

        Assert.Equal("2026-03-14", _db.Scalar("SELECT IssueDate FROM Documents"));
    }

    [Fact]
    public void Enums_are_stored_as_their_numbers()
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = TestData.NewDocument(partner.Id);
        document.Type = DocType.Otro;
        document.Direction = Direction.Emitida;
        _db.Save(document);
        var operation = TestData.NewFileOperation(document.Id);
        operation.State = FileOperationState.Failed;
        _db.Save(operation);

        Assert.Equal(9L, _db.Scalar("SELECT Type FROM Documents"));
        Assert.Equal(2L, _db.Scalar("SELECT Direction FROM Documents"));
        Assert.Equal(3L, _db.Scalar("SELECT State FROM FileOperations"));
    }
}
