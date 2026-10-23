using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

[Collection(CultureCollection.Name)]
public sealed class TrashViewModelTests : IDisposable
{
    private static readonly DateTime Deleted = new(2026, 10, 20, 8, 30, 0, DateTimeKind.Utc);

    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeDocumentService _documents = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<DocumentsChangedMessage> _changes = [];
    private readonly TrashViewModel _vm;

    public TrashViewModelTests()
    {
        _vm = new TrashViewModel(_documents, _dialogs, new Localizer(), _messenger);
        _messenger.Register<DocumentsChangedMessage>(this, (_, message) => _changes.Add(message));
        _documents.Trash.AddRange([InTrash(3, "Repsol S.A."), InTrash(1, "Iberdrola")]);
    }

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public async Task It_lists_the_deleted_documents_when_shown()
    {
        await _vm.OnNavigatedToAsync();

        Assert.Equal("Papelera", _vm.Title);
        Assert.Equal([3, 1], _vm.Items.Select(i => i.Id));
        Assert.Equal("2 documentos en la papelera", _vm.StatusText);
        Assert.False(_vm.IsEmpty);
    }

    [Fact]
    public async Task An_empty_trash_says_so()
    {
        _documents.Trash.Clear();

        await _vm.OnNavigatedToAsync();

        Assert.True(_vm.IsEmpty);
        Assert.Empty(_vm.Items);
    }

    [Fact]
    public async Task Restore_puts_it_back_tells_the_other_pages_and_reloads()
    {
        await _vm.OnNavigatedToAsync();

        await _vm.RestoreCommand.ExecuteAsync(_vm.Items[0]);

        Assert.Equal([3], _documents.Restored);
        Assert.Equal([new DocumentsChangedMessage(DocumentsChange.Restored, 3)], _changes);
        Assert.Equal([1], _vm.Items.Select(i => i.Id));
        Assert.Equal("1 documento en la papelera", _vm.StatusText);
    }

    [Fact]
    public async Task A_restore_that_fails_says_why_and_the_document_stays()
    {
        _documents.RestoreResult = Result.Fail(ErrorCode.ArchiveUnavailable);
        await _vm.OnNavigatedToAsync();

        await _vm.RestoreCommand.ExecuteAsync(_vm.Items[0]);

        Assert.Equal([new Localizer().Error(ErrorCode.ArchiveUnavailable)], _dialogs.Errors);
        Assert.Empty(_changes);
        Assert.Equal([3, 1], _vm.Items.Select(i => i.Id));
    }

    private static DocumentDetails InTrash(int id, string partner) => new(id, partner, $"N-{id}", new DateOnly(2026, 2, id),
        DocType.Factura, Direction.Recibida, $"_Papelera/2026/1T/Recibidas/{partner}/{id}.pdf", $"{id}.pdf", Deleted, Deleted, Deleted);
}
