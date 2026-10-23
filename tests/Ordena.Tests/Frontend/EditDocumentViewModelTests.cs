using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// The edit dialog with its services faked: what it shows, and what it sends when saved.
[Collection(CultureCollection.Name)]
public sealed class EditDocumentViewModelTests : IDisposable
{
    private const string Path = @"D:\Facturas\2026\1T\Emitidas\Iberdrola\2026-03-14_Iberdrola_F-77_Recibo.pdf";
    private static readonly DateTime Imported = new(2026, 3, 15, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DocumentDetails Iberdrola = new(5, "Iberdrola", "F-77", new DateOnly(2026, 3, 14),
        DocType.Recibo, Direction.Emitida, "2026/1T/Emitidas/Iberdrola/2026-03-14_Iberdrola_F-77_Recibo.pdf",
        "scan.pdf", Imported, Imported, null);

    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeDocumentService _documents = new();
    private readonly FakePreview _preview = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<DocumentsChangedMessage> _changes = [];
    private readonly EditDocumentViewModel _vm;
    private int _completed;

    public EditDocumentViewModelTests()
    {
        var localizer = new Localizer();
        var form = new DocumentFormViewModel(new FakePartnerService("Iberdrola"), localizer) { SuggestionDelay = TimeSpan.Zero };
        _vm = new EditDocumentViewModel(_documents, _preview, _dialogs, localizer, _messenger, form);
        _vm.Completed += (_, _) => _completed++;
        _messenger.Register<DocumentsChangedMessage>(this, (_, message) => _changes.Add(message));
        _documents.Details[5] = Iberdrola;
        _documents.FullPaths[5] = Path;
    }

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public async Task It_shows_the_documents_values_its_file_and_its_preview()
    {
        await _vm.LoadAsync(5);

        Assert.Equal("Iberdrola", _vm.Form.PartnerName);
        Assert.Equal("F-77", _vm.Form.DocNumber);
        Assert.Equal(new DateTime(2026, 3, 14), _vm.Form.IssueDate);
        Assert.Equal(DocType.Recibo, _vm.Form.SelectedType.Type);
        Assert.True(_vm.Form.IsIssued);
        Assert.False(_vm.Form.HasErrors);
        Assert.Equal(Iberdrola.RelativePath, _vm.FileText);
        Assert.Equal($"preview:{Path}", _vm.PreviewPath);
        Assert.False(_vm.IsFileMissing);
    }

    [Fact]
    public async Task A_missing_file_is_said_up_front_and_has_no_preview()
    {
        _documents.FullPaths.Remove(5);

        await _vm.LoadAsync(5);

        Assert.True(_vm.IsFileMissing);
        Assert.Null(_vm.PreviewPath);
    }

    [Fact]
    public async Task Saving_sends_every_field_and_tells_the_other_pages()
    {
        await _vm.LoadAsync(5);
        _vm.Form.DocNumber = "F-78";
        _vm.Form.IssueDate = new DateTime(2026, 4, 2);
        _vm.Form.IsReceived = true;

        await _vm.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_documents.Edits);
        Assert.Equal(new EditDocumentRequest
        {
            Id = 5,
            PartnerName = "Iberdrola",
            DocNumber = "F-78",
            IssueDate = new DateOnly(2026, 4, 2),
            Type = DocType.Recibo,
            Direction = Direction.Recibida,
        }, request);
        Assert.Equal([new DocumentsChangedMessage(DocumentsChange.Edited, 5)], _changes);
        Assert.Equal(1, _completed);
    }

    [Fact]
    public async Task An_incomplete_form_is_not_sent()
    {
        await _vm.LoadAsync(5);
        _vm.Form.DocNumber = " ";

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_documents.Edits);
        Assert.Equal(0, _completed);
    }

    [Fact]
    public async Task A_duplicate_number_is_saved_once_the_user_confirms()
    {
        _documents.EditAnswer = request => request.ConfirmDuplicate
            ? Result.Ok(new DocumentListItem(5, request.IssueDate, request.PartnerName, request.DocNumber, request.Type, request.Direction, "x.pdf"))
            : Result.Fail<DocumentListItem>(ErrorCode.DuplicateNumber);
        await _vm.LoadAsync(5);

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([false, true], _documents.Edits.Select(e => e.ConfirmDuplicate));
        Assert.StartsWith("«Iberdrola» ya tiene un documento con el número F-77.", Assert.Single(_dialogs.Questions));
        Assert.Equal(1, _completed);
    }

    [Fact]
    public async Task A_duplicate_number_the_user_rejects_keeps_the_dialog_open()
    {
        _documents.EditAnswer = _ => Result.Fail<DocumentListItem>(ErrorCode.DuplicateNumber);
        _dialogs.Confirm = false;
        await _vm.LoadAsync(5);

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_documents.Edits);
        Assert.Equal(0, _completed);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task A_failed_save_says_why_and_keeps_the_dialog_open()
    {
        _documents.EditAnswer = _ => Result.Fail<DocumentListItem>(ErrorCode.FileLocked);
        await _vm.LoadAsync(5);

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal([new Localizer().Error(ErrorCode.FileLocked)], _dialogs.Errors);
        Assert.Equal(0, _completed);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task Closing_releases_the_preview_copy()
    {
        await _vm.LoadAsync(5);

        _vm.Release();

        Assert.Equal([$"preview:{Path}"], _preview.Released);
        Assert.Null(_vm.PreviewPath);
    }
}
