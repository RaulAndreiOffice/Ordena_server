namespace Ordena.Backend.Repositories;

// One unit of work per service call: a short-lived DbContext, so nothing goes stale between calls.
// Usage: await using var uow = _uowFactory.Create();
internal interface IUnitOfWork : IAsyncDisposable
{
    IDocumentRepository Documents { get; }
    IPartnerRepository Partners { get; }
    IFileOperationRepository FileOperations { get; }
    ISettingsRepository Settings { get; }

    // Everything added or changed through the repositories, committed in one transaction.
    Task SaveChangesAsync(CancellationToken ct);
}
