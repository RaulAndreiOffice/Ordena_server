using Ordena.Backend.Data;

namespace Ordena.Backend.Repositories;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db)
    {
        _db = db;
        Documents = new DocumentRepository(db);
        Partners = new PartnerRepository(db);
        FileOperations = new FileOperationRepository(db);
        Settings = new SettingsRepository(db);
    }

    public IDocumentRepository Documents { get; }
    public IPartnerRepository Partners { get; }
    public IFileOperationRepository FileOperations { get; }
    public ISettingsRepository Settings { get; }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
