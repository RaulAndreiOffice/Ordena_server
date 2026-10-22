using Ordena.Backend.Models;

namespace Ordena.Backend.Repositories;

internal interface IFileOperationRepository
{
    void Add(FileOperation operation);

    // Only to undo an import whose file couldn't be written.
    void Remove(FileOperation operation);

    // Tracked, oldest first: startup recovery resolves each one and marks it Done or Failed.
    Task<IReadOnlyList<FileOperation>> GetPendingAsync(CancellationToken ct);
}
