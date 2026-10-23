using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// The import queue with every service faked: what comes up, what is sent, what each answer does.
[Collection(CultureCollection.Name)]
public sealed class ImportViewModelTests : IDisposable
{
    private const string A = @"C:\Descargas\a.pdf";
    private const string B = @"C:\Descargas\b.pdf";
    private const string C = @"C:\Descargas\c.pdf";

    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeImportService _import = new();
    private readonly FakeDocumentService _documents = new();
    private readonly FakePreview _preview = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeShell _shell = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<DocumentsChangedMessage> _changes = [];
    private readonly ImportViewModel _vm;

    public ImportViewModelTests()
    {
        var localizer = new Localizer();
        var form = new DocumentFormViewModel(new FakePartnerService("Iberdrola"), localizer) { SuggestionDelay = TimeSpan.Zero };
        _vm = new ImportViewModel(_import, _documents, _preview, _picker, _dialogs, _shell, localizer, _messenger, form);
        _messenger.Register<DocumentsChangedMessage>(this, (_, message) => _changes.Add(message));
    }

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public void Before_any_file_the_page_shows_the_drop_zone()
    {
        Assert.False(_vm.HasQueue);
        Assert.False(_vm.IsFinished);
        Assert.False(_vm.CanFill);
        Assert.Equal("Importar", _vm.Title);
    }

    [Fact]
    public void Dropped_pdfs_are_queued_once_and_the_first_comes_up_checked()
    {
        _messenger.Send(new FilesDroppedMessage([A, B, @"C:\Descargas\notas.txt", A.ToUpperInvariant()]));

        Assert.Equal(["a.pdf", "b.pdf"], _vm.Queue.Select(i => i.FileName));
        Assert.All(_vm.Queue, i => Assert.Equal(ImportState.Pending, i.State));
        Assert.Same(_vm.Queue[0], _vm.CurrentItem);
        Assert.Equal([true, false], _vm.Queue.Select(i => i.IsCurrent));
        Assert.Equal($"preview:{A}", _vm.PreviewPath);
        Assert.Equal([A], _import.Checked);
        Assert.True(_vm.HasQueue);
        Assert.True(_vm.CanFill);
        Assert.False(_vm.HasProblem);
        Assert.Equal("Archivo 1 de 2 · a.pdf", _vm.PositionText);
        Assert.Equal(1, _vm.Form.FocusRequest);
    }

    [Fact]
    public async Task Files_added_later_join_the_queue_behind_the_current_one()
    {
        await _vm.EnqueueAsync([A, B]);

        await _vm.EnqueueAsync([C, B]);

        Assert.Equal(["a.pdf", "b.pdf", "c.pdf"], _vm.Queue.Select(i => i.FileName));
        Assert.Same(_vm.Queue[0], _vm.CurrentItem);
        Assert.Equal("Archivo 1 de 3 · a.pdf", _vm.PositionText);
        Assert.Equal([A], _import.Checked);
    }

    [Fact]
    public async Task Add_files_queues_what_is_picked_and_nothing_when_cancelled()
    {
        await _vm.AddFilesCommand.ExecuteAsync(null);
        Assert.False(_vm.HasQueue);

        _picker.Files = [A, B];
        await _vm.AddFilesCommand.ExecuteAsync(null);

        Assert.Equal(["a.pdf", "b.pdf"], _vm.Queue.Select(i => i.FileName));
        Assert.Same(_vm.Queue[0], _vm.CurrentItem);
    }

    [Fact]
    public async Task Saving_files_the_document_and_brings_up_the_next()
    {
        await _vm.EnqueueAsync([A, B]);
        Fill();
        _vm.Form.SelectedType = _vm.Form.Types.Single(t => t.Type == DocType.Recibo);

        await _vm.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_import.Requests);
        Assert.Equal(A, request.SourcePath);
        Assert.Equal("Iberdrola", request.PartnerName);
        Assert.Equal("F-77", request.DocNumber);
        Assert.Equal(new DateOnly(2026, 3, 14), request.IssueDate);
        Assert.Equal(DocType.Recibo, request.Type);
        Assert.Equal(Direction.Recibida, request.Direction);
        Assert.False(request.ConfirmDuplicate);

        Assert.Equal(ImportState.Saved, _vm.Queue[0].State);
        Assert.Equal("Archivado como 2026/1T/Recibidas/Iberdrola/a.pdf", _vm.Queue[0].Message);
        Assert.Equal([new DocumentsChangedMessage(DocumentsChange.Imported, 1)], _changes);

        Assert.Same(_vm.Queue[1], _vm.CurrentItem);
        Assert.Equal([false, true], _vm.Queue.Select(i => i.IsCurrent));
        Assert.Equal($"preview:{B}", _vm.PreviewPath);
        Assert.Equal([$"preview:{A}"], _preview.Released.OfType<string>());
        Assert.Equal("Archivo 2 de 2 · b.pdf", _vm.PositionText);

        // Ready for the next file, with the same type.
        Assert.Equal("", _vm.Form.PartnerName);
        Assert.Null(_vm.Form.IssueDate);
        Assert.Equal(DocType.Recibo, _vm.Form.SelectedType.Type);
    }

    [Fact]
    public async Task A_filed_document_whose_original_stayed_says_so()
    {
        _import.Answer = request => FakeImportService.Archived(request, 1, sourceDeleted: false);
        await _vm.EnqueueAsync([A]);
        Fill();

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ImportState.Saved, _vm.Queue[0].State);
        Assert.Equal(
            "Archivado como 2026/1T/Recibidas/Iberdrola/a.pdf. El original no se pudo borrar: quizá está abierto en otro programa.",
            _vm.Queue[0].Message);
    }

    [Fact]
    public async Task A_copy_mode_item_sends_KeepSource_and_gets_the_plain_saved_message()
    {
        _import.Answer = request => FakeImportService.Archived(request, 1, sourceDeleted: false);
        _messenger.Send(new FilesDroppedMessage([A], KeepSource: true));
        Fill();

        await _vm.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_import.Requests);
        Assert.True(request.KeepSource);
        Assert.Equal(ImportState.Saved, _vm.Queue[0].State);
        Assert.Equal("Archivado como 2026/1T/Recibidas/Iberdrola/a.pdf", _vm.Queue[0].Message);
    }

    [Fact]
    public async Task An_incomplete_form_is_not_sent()
    {
        await _vm.EnqueueAsync([A]);
        _vm.Form.DocNumber = "F-77";

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_import.Requests);
        Assert.True(_vm.Form.HasErrors);
        Assert.Same(_vm.Queue[0], _vm.CurrentItem);
        Assert.Equal(ImportState.Pending, _vm.Queue[0].State);
    }

    [Fact]
    public async Task A_duplicate_number_is_filed_once_the_user_confirms()
    {
        _import.Answer = request => request.ConfirmDuplicate
            ? FakeImportService.Archived(request, 1)
            : Result.Fail<ImportResult>(ErrorCode.DuplicateNumber);
        await _vm.EnqueueAsync([A]);
        Fill();

        await _vm.SaveCommand.ExecuteAsync(null);

        var question = Assert.Single(_dialogs.Questions);
        Assert.StartsWith("«Iberdrola» ya tiene un documento con el número F-77.", question);
        Assert.Equal([false, true], _import.Requests.Select(r => r.ConfirmDuplicate));
        Assert.Equal(ImportState.Saved, _vm.Queue[0].State);
    }

    [Fact]
    public async Task A_duplicate_number_the_user_rejects_stays_on_screen_as_typed()
    {
        _import.Answer = _ => Result.Fail<ImportResult>(ErrorCode.DuplicateNumber);
        _dialogs.Confirm = false;
        await _vm.EnqueueAsync([A]);
        Fill();

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(_import.Requests);
        Assert.Same(_vm.Queue[0], _vm.CurrentItem);
        Assert.Equal(ImportState.Pending, _vm.Queue[0].State);
        Assert.Equal("F-77", _vm.Form.DocNumber);
    }

    // Problems with what was typed, or with the archive: the message, and the file stays to fix it.
    [Fact]
    public async Task Any_other_failure_is_shown_and_the_file_stays()
    {
        _import.Answer = _ => Result.Fail<ImportResult>(ErrorCode.InvalidPartnerName);
        await _vm.EnqueueAsync([A, B]);
        Fill();

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["El nombre del proveedor o cliente no es válido."], _dialogs.Errors);
        Assert.Same(_vm.Queue[0], _vm.CurrentItem);
        Assert.Equal(ImportState.Pending, _vm.Queue[0].State);
        Assert.Equal("Iberdrola", _vm.Form.PartnerName);
    }

    [Fact]
    public async Task An_already_archived_file_offers_to_open_the_archived_one_or_skip()
    {
        _import.Checks[A] = Result.Fail(ErrorCode.AlreadyArchived, "7");
        _documents.FullPaths[7] = @"D:\Facturas\2026\1T\Recibidas\Iberdrola\2026-03-14_Iberdrola_F-77.pdf";
        await _vm.EnqueueAsync([A, B]);

        Assert.True(_vm.HasProblem);
        Assert.False(_vm.CanFill);
        Assert.True(_vm.CanOpenArchived);
        Assert.False(_vm.CanRestore);
        Assert.Equal("Este archivo ya está en el archivo de facturas.", _vm.ProblemText);
        Assert.False(_vm.SaveCommand.CanExecute(null));

        await _vm.OpenArchivedCommand.ExecuteAsync(null);
        Assert.Equal([_documents.FullPaths[7]], _shell.Opened);

        await _vm.SkipCommand.ExecuteAsync(null);
        Assert.Equal(ImportState.Skipped, _vm.Queue[0].State);
        Assert.Equal("Este archivo ya está en el archivo de facturas.", _vm.Queue[0].Message);
        Assert.Same(_vm.Queue[1], _vm.CurrentItem);
        Assert.False(_vm.HasProblem);
        Assert.True(_vm.CanFill);
    }

    [Fact]
    public async Task A_file_in_the_trash_can_be_restored_from_there()
    {
        _import.Checks[A] = Result.Fail(ErrorCode.AlreadyInTrash, "9");
        await _vm.EnqueueAsync([A, B]);
        Assert.True(_vm.CanRestore);
        Assert.Equal("Este archivo está en la papelera.", _vm.ProblemText);

        await _vm.RestoreCommand.ExecuteAsync(null);

        Assert.Equal([9], _documents.Restored);
        Assert.Equal(ImportState.Saved, _vm.Queue[0].State);
        Assert.Equal("Restaurado de la papelera", _vm.Queue[0].Message);
        Assert.Equal([new DocumentsChangedMessage(DocumentsChange.Restored, 9)], _changes);
        Assert.Same(_vm.Queue[1], _vm.CurrentItem);
    }

    [Fact]
    public async Task A_failed_restore_is_shown_and_the_file_stays()
    {
        _import.Checks[A] = Result.Fail(ErrorCode.AlreadyInTrash, "9");
        _documents.RestoreResult = Result.Fail(ErrorCode.FileLocked);
        await _vm.EnqueueAsync([A]);

        await _vm.RestoreCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Errors);
        Assert.Same(_vm.Queue[0], _vm.CurrentItem);
        Assert.True(_vm.CanRestore);
        Assert.Empty(_changes);
    }

    // Nothing to do about these: the file is marked and the queue moves on by itself.
    [Theory]
    [InlineData(ErrorCode.NotPdf, "El archivo no es un PDF.")]
    [InlineData(ErrorCode.Empty, "El archivo está vacío.")]
    [InlineData(ErrorCode.FileMissing, "Fichero no encontrado.")]
    public async Task A_file_that_cannot_be_imported_is_marked_and_skipped(ErrorCode error, string message)
    {
        _import.Checks[A] = Result.Fail(error);
        await _vm.EnqueueAsync([A, B]);

        Assert.Equal(ImportState.Failed, _vm.Queue[0].State);
        Assert.Equal(message, _vm.Queue[0].Message);
        Assert.Same(_vm.Queue[1], _vm.CurrentItem);
        Assert.Equal([$"preview:{A}"], _preview.Released.OfType<string>());
        Assert.Equal([A, B], _import.Checked);
    }

    [Fact]
    public async Task A_file_open_elsewhere_can_be_checked_again()
    {
        _import.Checks[A] = Result.Fail(ErrorCode.FileLocked);
        await _vm.EnqueueAsync([A]);
        Assert.True(_vm.CanRetry);
        Assert.Equal("El archivo está abierto en otro programa. Ciérralo y vuelve a intentarlo.", _vm.ProblemText);

        _import.Checks.Remove(A);
        await _vm.RetryCommand.ExecuteAsync(null);

        Assert.Equal([A, A], _import.Checked);
        Assert.False(_vm.HasProblem);
        Assert.True(_vm.CanFill);
    }

    // Another program archived or opened the file between the check and the save.
    [Fact]
    public async Task A_problem_found_only_when_saving_shows_the_same_banner()
    {
        _import.Answer = _ => Result.Fail<ImportResult>(ErrorCode.AlreadyArchived, "7");
        await _vm.EnqueueAsync([A]);
        Fill();

        await _vm.SaveCommand.ExecuteAsync(null);

        Assert.True(_vm.CanOpenArchived);
        Assert.Empty(_dialogs.Errors);
        Assert.Equal(ImportState.Pending, _vm.Queue[0].State);
    }

    [Fact]
    public async Task When_every_file_is_done_the_summary_shows_and_the_list_can_be_cleared()
    {
        await _vm.EnqueueAsync([A, B]);
        Fill();
        await _vm.SaveCommand.ExecuteAsync(null);
        await _vm.SkipCommand.ExecuteAsync(null);

        Assert.True(_vm.IsFinished);
        Assert.Null(_vm.CurrentItem);
        Assert.Null(_vm.PreviewPath);
        Assert.False(_vm.CanFill);
        Assert.Equal("", _vm.PositionText);
        Assert.Equal("Archivados: 1 · Omitidos: 1", _vm.Summary);
        Assert.Equal([$"preview:{A}", $"preview:{B}"], _preview.Released.OfType<string>());
        Assert.False(_vm.SkipCommand.CanExecute(null));

        _vm.ClearFinishedCommand.Execute(null);

        Assert.Empty(_vm.Queue);
        Assert.False(_vm.HasQueue);
        Assert.False(_vm.IsFinished);
    }

    [Fact]
    public async Task Clearing_finished_files_keeps_the_pending_ones()
    {
        await _vm.EnqueueAsync([A, B, C]);
        await _vm.SkipCommand.ExecuteAsync(null);

        _vm.ClearFinishedCommand.Execute(null);

        Assert.Equal(["b.pdf", "c.pdf"], _vm.Queue.Select(i => i.FileName));
        Assert.Same(_vm.Queue[0], _vm.CurrentItem);
        Assert.Equal("Archivo 1 de 2 · b.pdf", _vm.PositionText);
    }

    // The fallback when the preview can't be shown: the original file in the PDF viewer.
    [Fact]
    public async Task Open_in_viewer_opens_the_original_file()
    {
        _preview.Unreadable.Add(A);
        await _vm.EnqueueAsync([A]);
        Assert.Null(_vm.PreviewPath);

        _vm.OpenInViewerCommand.Execute(null);

        Assert.Equal([A], _shell.Opened);
    }

    private void Fill()
    {
        _vm.Form.PartnerName = "Iberdrola";
        _vm.Form.DocNumber = "F-77";
        _vm.Form.IssueDate = new DateTime(2026, 3, 14);
    }
}
