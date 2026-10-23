using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// Why the current file can't simply be filled in; the banner shows the matching actions.
public enum ImportProblem
{
    None,
    AlreadyArchived,
    AlreadyInTrash,
    FileLocked,
}

// The import screen: dropped PDFs are filled in and archived one at a time, each with its preview.
// A singleton, so the queue survives switching pages.
public sealed partial class ImportViewModel : PageViewModel, IRecipient<FilesDroppedMessage>
{
    private readonly IImportService _import;
    private readonly IDocumentService _documents;
    private readonly IPreviewService _preview;
    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly ILocalizer _localizer;
    private readonly IMessenger _messenger;

    // The archived document the banner is about (Abrir / Restaurar).
    private int? _problemDocumentId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFill), nameof(IsFinished))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SkipCommand), nameof(OpenInViewerCommand))]
    private ImportQueueItem? _currentItem;

    // The temporary copy shown by the preview.
    [ObservableProperty]
    private string? _previewPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFill), nameof(HasProblem), nameof(CanOpenArchived), nameof(CanRestore), nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private ImportProblem _problem;

    [ObservableProperty]
    private string? _problemText;

    // "Archivo 2 de 5 · scan.pdf"
    [ObservableProperty]
    private string _positionText = "";

    // "Archivados: 4 · Omitidos: 1"
    [ObservableProperty]
    private string _summary = "";

    public ImportViewModel(
        IImportService import,
        IDocumentService documents,
        IPreviewService preview,
        IFilePickerService picker,
        IDialogService dialogs,
        IShellService shell,
        ILocalizer localizer,
        IMessenger messenger,
        DocumentFormViewModel form)
    {
        _import = import;
        _documents = documents;
        _preview = preview;
        _picker = picker;
        _dialogs = dialogs;
        _shell = shell;
        _localizer = localizer;
        _messenger = messenger;
        Form = form;
        messenger.Register<FilesDroppedMessage>(this);
    }

    public override string Title => _localizer.Get("Page_Import");

    public DocumentFormViewModel Form { get; }

    public ObservableCollection<ImportQueueItem> Queue { get; } = [];

    public bool HasQueue => Queue.Count > 0;

    // Every file is done: the screen shows the summary.
    public bool IsFinished => HasQueue && CurrentItem is null;

    // The form is shown for the current file.
    public bool CanFill => CurrentItem is not null && Problem == ImportProblem.None;

    public bool HasProblem => Problem != ImportProblem.None;
    public bool CanOpenArchived => Problem == ImportProblem.AlreadyArchived;
    public bool CanRestore => Problem == ImportProblem.AlreadyInTrash;
    public bool CanRetry => Problem == ImportProblem.FileLocked;

    internal Task PendingEnqueue { get; private set; } = Task.CompletedTask;

    // async void like a command's handler, so an unexpected error reaches App's handler instead of vanishing.
    public async void Receive(FilesDroppedMessage message) => await (PendingEnqueue = EnqueueAsync(message.Paths, message.KeepSource));

    // Adds PDFs not queued yet; the first one becomes current when nothing is.
    public async Task EnqueueAsync(IEnumerable<string> paths, bool keepSource = false)
    {
        foreach (var path in paths)
        {
            var isPdf = string.Equals(System.IO.Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
            if (isPdf && !Queue.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                Queue.Add(new ImportQueueItem(path, keepSource));
        }

        if (CurrentItem is null)
            await ShowNextAsync();
        else
            UpdateTexts();
    }

    [RelayCommand(CanExecute = nameof(CanFill))]
    private async Task SaveAsync()
    {
        var item = CurrentItem;
        if (item is null || !Form.Validate())
            return;

        var result = await _import.ImportAsync(Form.ToImportRequest(item.Path, confirmDuplicate: false, item.KeepSource), CancellationToken.None);
        if (result.Error == ErrorCode.DuplicateNumber)
        {
            var question = _localizer.Format("Import_AskDuplicate", Form.PartnerName.Trim(), Form.DocNumber.Trim());
            if (!await _dialogs.ConfirmAsync(question))
                return;
            result = await _import.ImportAsync(Form.ToImportRequest(item.Path, confirmDuplicate: true, item.KeepSource), CancellationToken.None);
        }

        if (result.IsSuccess)
        {
            var imported = result.Value;
            // A copy keeps its original on purpose; otherwise a kept original couldn't be deleted.
            var key = imported.SourceDeleted || item.KeepSource ? "Import_SavedAs" : "Import_SavedSourceKept";
            Finish(item, ImportState.Saved, _localizer.Format(key, imported.Document.RelativePath));
            _messenger.Send(new DocumentsChangedMessage(DocumentsChange.Imported, imported.Document.Id));
            Form.Reset();
            await ShowNextAsync();
            return;
        }

        if (result.Error.Value is ErrorCode.AlreadyArchived or ErrorCode.AlreadyInTrash or ErrorCode.FileLocked
            or ErrorCode.NotPdf or ErrorCode.Empty or ErrorCode.FileMissing)
            await HandleFileProblemAsync(item, result.Error.Value, result.Detail);
        else
            await _dialogs.ShowErrorAsync(_localizer.Error(result.Error.Value)); // stays on the file
    }

    [RelayCommand(CanExecute = nameof(HasCurrentItem))]
    private async Task SkipAsync()
    {
        var item = CurrentItem!;
        Finish(item, ImportState.Skipped, Problem == ImportProblem.None ? null : ProblemText);
        Form.Reset();
        await ShowNextAsync();
    }

    [RelayCommand]
    private Task AddFilesAsync() => EnqueueAsync(_picker.PickPdfFiles(
        _localizer.Get("Import_PickFilesTitle"), _localizer.Get("Import_PdfFilter")));

    // Removes the files already dealt with; pending ones stay.
    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var item in Queue.Where(i => i.State != ImportState.Pending).ToList())
            Queue.Remove(item);
        UpdateTexts();
    }

    [RelayCommand]
    private async Task OpenArchivedAsync()
    {
        if (_problemDocumentId is not { } id)
            return;

        var path = await _documents.GetFullPathAsync(id, CancellationToken.None);
        if (path.IsSuccess)
            _shell.OpenFile(path.Value);
        else
            await _dialogs.ShowErrorAsync(_localizer.Error(path.Error.Value));
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (CurrentItem is not { } item || _problemDocumentId is not { } id)
            return;

        var restored = await _documents.RestoreAsync(id, CancellationToken.None);
        if (!restored.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(_localizer.Error(restored.Error.Value));
            return;
        }

        Finish(item, ImportState.Saved, _localizer.Get("Import_Restored"));
        _messenger.Send(new DocumentsChangedMessage(DocumentsChange.Restored, id));
        await ShowNextAsync();
    }

    // After "El archivo está abierto en otro programa": check the file again.
    [RelayCommand]
    private Task RetryAsync() => CheckCurrentAsync();

    // When the preview can't be shown (no WebView2 Runtime).
    [RelayCommand(CanExecute = nameof(HasCurrentItem))]
    private void OpenInViewer() => _shell.OpenFile(CurrentItem!.Path);

    private bool HasCurrentItem() => CurrentItem is not null;

    // The next pending file becomes current: its preview, then the early check.
    private async Task ShowNextAsync()
    {
        if (CurrentItem is not null)
            CurrentItem.IsCurrent = false;
        _preview.Release(PreviewPath);
        PreviewPath = null;
        SetProblem(ImportProblem.None, null, null);

        CurrentItem = Queue.FirstOrDefault(i => i.State == ImportState.Pending);
        UpdateTexts();
        if (CurrentItem is not { } item)
            return;

        item.IsCurrent = true;
        PreviewPath = await _preview.CreateCopyAsync(item.Path);
        await CheckCurrentAsync();
    }

    // Before anything is typed: already archived, not a PDF, open elsewhere…
    private async Task CheckCurrentAsync()
    {
        if (CurrentItem is not { } item)
            return;

        var check = await _import.CheckAsync(item.Path, CancellationToken.None);
        if (check.IsSuccess)
        {
            SetProblem(ImportProblem.None, null, null);
            Form.FocusRequest++;
        }
        else
        {
            await HandleFileProblemAsync(item, check.Error.Value, check.Detail);
        }
    }

    // What the check or a save found wrong with the file itself.
    private async Task HandleFileProblemAsync(ImportQueueItem item, ErrorCode error, string? detail)
    {
        switch (error)
        {
            case ErrorCode.AlreadyArchived:
                SetProblem(ImportProblem.AlreadyArchived, _localizer.Get("Import_AlreadyArchived"), ParseId(detail));
                break;
            case ErrorCode.AlreadyInTrash:
                SetProblem(ImportProblem.AlreadyInTrash, _localizer.Get("Import_AlreadyInTrash"), ParseId(detail));
                break;
            case ErrorCode.FileLocked:
                SetProblem(ImportProblem.FileLocked, _localizer.Error(error), null);
                break;
            default:
                // Not a PDF, empty, gone: this file can't be imported, so the queue moves on.
                Finish(item, ImportState.Failed, _localizer.Error(error));
                await ShowNextAsync();
                break;
        }
    }

    private void SetProblem(ImportProblem problem, string? text, int? documentId)
    {
        Problem = problem;
        ProblemText = text;
        _problemDocumentId = documentId;
    }

    private void Finish(ImportQueueItem item, ImportState state, string? message)
    {
        item.State = state;
        item.Message = message;
    }

    private void UpdateTexts()
    {
        PositionText = CurrentItem is null
            ? ""
            : _localizer.Format("Import_Position", Queue.IndexOf(CurrentItem) + 1, Queue.Count, CurrentItem.FileName);
        Summary = _localizer.Format("Import_Summary",
            Queue.Count(i => i.State == ImportState.Saved),
            Queue.Count(i => i.State is ImportState.Skipped or ImportState.Failed));
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(IsFinished));
    }

    private static int? ParseId(string? detail) =>
        int.TryParse(detail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}
