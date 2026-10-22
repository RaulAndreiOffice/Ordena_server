using Ordena.Backend.Models;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Repositories;

public sealed class FileOperationRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetPending_returns_only_pending_rows_oldest_first()
    {
        var partner = _db.Save(TestData.NewPartner());
        var document = _db.Save(TestData.NewDocument(partner.Id));
        var first = _db.Save(Operation(document.Id, FileOperationState.Pending));
        _db.Save(Operation(document.Id, FileOperationState.Done));
        _db.Save(Operation(document.Id, FileOperationState.Failed));
        var last = _db.Save(Operation(document.Id, FileOperationState.Pending));

        await using var uow = _db.UnitOfWorkFactory.Create();
        var pending = await uow.FileOperations.GetPendingAsync(Ct);

        Assert.Equal([first.Id, last.Id], pending.Select(f => f.Id));
    }

    private static FileOperation Operation(int documentId, FileOperationState state)
    {
        var operation = TestData.NewFileOperation(documentId);
        operation.State = state;
        return operation;
    }
}
