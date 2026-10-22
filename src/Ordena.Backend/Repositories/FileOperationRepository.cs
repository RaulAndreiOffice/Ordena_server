using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Data;
using Ordena.Backend.Models;

namespace Ordena.Backend.Repositories;

internal sealed class FileOperationRepository(AppDbContext db) : IFileOperationRepository
{
    public void Add(FileOperation operation) => db.FileOperations.Add(operation);

    public void Remove(FileOperation operation) => db.FileOperations.Remove(operation);

    public async Task<IReadOnlyList<FileOperation>> GetPendingAsync(CancellationToken ct) =>
        await db.FileOperations
            .Where(f => f.State == FileOperationState.Pending)
            .OrderBy(f => f.Id)
            .ToListAsync(ct).ConfigureAwait(false);
}
