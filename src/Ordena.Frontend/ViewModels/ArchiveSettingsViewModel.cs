using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// Ajustes' archive cards: where the invoices folder is ("Cambiar ubicación", "Mover carpeta"), the backups,
// and "Comprobar carpeta" with what it found. Nothing here runs while a bulk import is copying files in.
public sealed partial class ArchiveSettingsViewModel : ObservableObject
{
    private readonly IMaintenanceService _maintenance;
    private readonly IArchiveService _archive;
    private readonly ISettingsService _settings;
    private readonly IDocumentService _documents;
    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly IMessenger _messenger;
    private readonly INavigationService _navigation;

    // "Última copia: 24/10/2026, 14:21"
    [ObservableProperty]
    private string _lastBackupText = "";

    // Set once "Comprobar carpeta" has run: its results show below the button.
    [ObservableProperty]
    private bool _hasCheckReport;

    // "Todo en orden: 3.001 documentos, cada uno con su archivo.", or "Comprobados 3.001 documentos."
    [ObservableProperty]
    private string _checkSummary = "";

    // "Devueltos a su sitio: 2", for files moved or renamed by hand; empty when there were none.
    [ObservableProperty]
    private string _relinkedText = "";

    public ArchiveSettingsViewModel(
        IArchiveState folder,
        IMaintenanceService maintenance,
        IArchiveService archive,
        ISettingsService settings,
        IDocumentService documents,
        IFilePickerService picker,
        IDialogService dialogs,
        ILocalizer localizer,
        IMessenger messenger,
        INavigationService navigation)
    {
        Folder = folder;
        _maintenance = maintenance;
        _archive = archive;
        _settings = settings;
        _documents = documents;
        _picker = picker;
        _dialogs = dialogs;
        _localizer = localizer;
        _messenger = messenger;
        _navigation = navigation;

        MissingDocuments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(MissingTitle));
            OnPropertyChanged(nameof(HasMissingDocuments));
        };
        UnknownFiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(UnknownTitle));
            OnPropertyChanged(nameof(HasUnknownFiles));
            RegisterCommand.NotifyCanExecuteChanged();
        };
    }

    // Where the archive is and whether it is there.
    public IArchiveState Folder { get; }

    // "Disponible" or "No disponible", next to the path.
    public string AvailabilityText => _localizer.Get(Folder.IsAvailable ? "Settings_FolderAvailable" : "Settings_FolderUnavailable");

    public ObservableCollection<MissingDocumentItem> MissingDocuments { get; } = [];

    public ObservableCollection<UnknownFileItem> UnknownFiles { get; } = [];

    public bool HasMissingDocuments => MissingDocuments.Count > 0;

    public bool HasUnknownFiles => UnknownFiles.Count > 0;

    // "Documentos sin archivo: 1"
    public string MissingTitle => _localizer.Format("Settings_CheckMissing", MissingDocuments.Count);

    // "PDF que no están en Ordena: 3"
    public string UnknownTitle => _localizer.Format("Settings_CheckUnknown", UnknownFiles.Count);

    // The page is shown: follow the folder's state while it is.
    public async Task LoadAsync()
    {
        Folder.PropertyChanged += OnFolderChanged;
        await LoadLastBackupAsync();
    }

    // The page is left: the app-wide state no longer holds on to it.
    public void Unload() => Folder.PropertyChanged -= OnFolderChanged;

    // "Cambiar ubicación…": the user moved the folder themselves and shows where to.
    [RelayCommand]
    private async Task ChangeLocationAsync()
    {
        if (await RefuseWhileImportingAsync())
            return;
        if (await Folder.LocateAsync())
            await _dialogs.ShowInfoAsync(_localizer.Format("Settings_LocationChanged", Folder.Root));
    }

    // "Mover carpeta…": every file is copied to the new place and checked, and only then does Ordena switch
    // to it. The old folder is deleted afterwards, if the user agrees.
    [RelayCommand(CanExecute = nameof(IsFolderAvailable))]
    private async Task MoveAsync()
    {
        if (await RefuseWhileImportingAsync() || Folder.Root is not { } oldRoot)
            return;
        var chosen = _picker.PickFolder(_localizer.Get("Settings_MovePickTitle"), Path.GetDirectoryName(oldRoot));
        if (chosen is null)
            return;
        var target = MoveTarget(chosen, oldRoot);
        if (!await _dialogs.ConfirmAsync(_localizer.Format("Settings_MoveConfirm", target)))
            return;

        Result<ArchiveMoveReport> moved;
        try
        {
            moved = await _dialogs.RunWithProgressAsync(_localizer.Get("Settings_MoveProgress"),
                (progress, ct) => _archive.MoveAsync(target, progress, ct));
        }
        catch (OperationCanceledException)
        {
            await _dialogs.ShowInfoAsync(_localizer.Get("Settings_MoveCancelled"));
            return;
        }
        if (!moved.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(MoveError(moved, target));
            return;
        }

        Folder.SetRoot(moved.Value.NewRoot);
        if (!await _dialogs.ConfirmAsync(_localizer.Format("Settings_MoveDone", moved.Value.NewRoot, moved.Value.OldRoot)))
            return;

        var deleted = await _archive.DeleteOldCopyAsync(moved.Value.OldRoot, CancellationToken.None);
        if (!deleted.IsSuccess)
            await _dialogs.ShowErrorAsync(_localizer.Error(deleted.Error.Value));
        else if (deleted.Value > 0)
            await _dialogs.ShowInfoAsync(_localizer.Format("Settings_OldCopyKept", deleted.Value, moved.Value.OldRoot));
        else
            await _dialogs.ShowInfoAsync(_localizer.Get("Settings_OldCopyDeleted"));
    }

    [RelayCommand(CanExecute = nameof(IsFolderAvailable))]
    private async Task BackupNowAsync()
    {
        var backup = await _maintenance.BackupNowAsync(CancellationToken.None);
        if (!backup.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(_localizer.Error(backup.Error.Value));
            return;
        }
        await LoadLastBackupAsync();
    }

    // "Comprobar carpeta": files moved or renamed by hand go back where they belong; what is still wrong is listed.
    [RelayCommand(CanExecute = nameof(IsFolderAvailable))]
    private async Task CheckAsync()
    {
        if (await RefuseWhileImportingAsync())
            return;

        Result<ArchiveCheckReport> check;
        try
        {
            check = await _dialogs.RunWithProgressAsync(_localizer.Get("Settings_CheckProgress"),
                (_, ct) => _maintenance.CheckArchiveAsync(ct));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!check.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(_localizer.Error(check.Error.Value));
            return;
        }
        ShowCheck(check.Value);
    }

    // "Buscar archivo" for a document the check found without its file.
    [RelayCommand]
    private async Task LocateFileAsync(MissingDocumentItem item)
    {
        var expected = Path.GetFullPath(Path.Combine(Folder.Root ?? "", item.Document.RelativePath));
        var chosen = _picker.PickPdfFile(
            _localizer.Get("Search_PickFileTitle"), _localizer.Get("Import_PdfFilter"), Path.GetDirectoryName(expected));
        if (chosen is null)
            return;

        var relinked = await _documents.RelinkAsync(item.Document.Id, chosen, CancellationToken.None);
        if (!relinked.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(_localizer.Error(relinked.Error.Value));
            return;
        }

        MissingDocuments.Remove(item);
        // One of the PDFs Ordena didn't know: it was this document's, and is now in its place.
        if (UnknownFiles.FirstOrDefault(f => string.Equals(f.Path, chosen, StringComparison.OrdinalIgnoreCase)) is { } found)
            UnknownFiles.Remove(found);
    }

    // "Añadir a Ordena": PDFs already named as Ordena names them are registered where they are; the rest go to
    // the import queue, which moves each one to its place once filled in.
    [RelayCommand(CanExecute = nameof(HasUnknownFiles))]
    private async Task RegisterAsync()
    {
        var registered = await _maintenance.RegisterFilesAsync(UnknownFiles.Select(f => f.Path).ToList(), CancellationToken.None);
        if (!registered.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(_localizer.Error(registered.Error.Value));
            return;
        }

        var report = registered.Value;
        UnknownFiles.Clear();
        if (report.Registered > 0)
            _messenger.Send(new DocumentsChangedMessage(DocumentsChange.Registered, 0));
        await _dialogs.ShowInfoAsync(_localizer.Format("Settings_Registered", report.Registered, report.Remaining.Count));

        if (report.Remaining.Count > 0)
        {
            await _navigation.NavigateToAsync<ImportViewModel>();
            _messenger.Send(new FilesDroppedMessage(report.Remaining));
        }
    }

    // The folder picked when it is empty (made for the archive); otherwise a folder named like the archive
    // inside it, as when a folder is moved into another in Explorer: "E:\" → "E:\Facturas".
    internal static string MoveTarget(string chosen, string oldRoot)
    {
        if (!Directory.Exists(chosen) || !Directory.EnumerateFileSystemEntries(chosen).Any())
            return chosen;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(oldRoot));
        return Path.Combine(chosen, name.Length > 0 ? name : "Facturas");
    }

    private bool IsFolderAvailable() => Folder.IsAvailable;

    private void OnFolderChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AvailabilityText));
        MoveCommand.NotifyCanExecuteChanged();
        BackupNowCommand.NotifyCanExecuteChanged();
        CheckCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> RefuseWhileImportingAsync()
    {
        if (!Folder.IsImporting)
            return false;
        await _dialogs.ShowErrorAsync(_localizer.Get("Settings_WaitForBulkImport"));
        return true;
    }

    private async Task LoadLastBackupAsync()
    {
        var last = (await _settings.GetAsync(CancellationToken.None)).LastBackupUtc;
        LastBackupText = last is { } utc
            ? _localizer.Format("Settings_LastBackup", utc.ToLocalTime())
            : _localizer.Get("Settings_NoBackupYet");
    }

    private void ShowCheck(ArchiveCheckReport report)
    {
        CheckSummary = report.Missing.Count == 0 && report.UnknownFiles.Count == 0
            ? _localizer.Format("Settings_CheckAllGood", report.DocumentCount)
            : _localizer.Format("Settings_CheckSummary", report.DocumentCount);
        RelinkedText = report.Relinked.Count == 0 ? "" : _localizer.Format("Settings_CheckRelinked", report.Relinked.Count);

        MissingDocuments.Clear();
        foreach (var document in report.Missing)
            MissingDocuments.Add(new MissingDocumentItem(document,
                _localizer.Format("Settings_MissingItem", document.PartnerName, document.DocNumber, document.IssueDate)));

        UnknownFiles.Clear();
        foreach (var path in report.UnknownFiles)
            UnknownFiles.Add(new UnknownFileItem(path, Path.GetRelativePath(Folder.Root ?? "", path)));

        HasCheckReport = true;
    }

    // A folder that couldn't be used, or a file that stopped the copy, named in the message.
    private string MoveError(Result<ArchiveMoveReport> moved, string target)
    {
        var error = moved.Error!.Value;
        return error switch
        {
            ErrorCode.TargetExists => _localizer.Format("Settings_MoveTargetNotEmpty", target),
            ErrorCode.FileLocked or ErrorCode.FileMissing or ErrorCode.IoError when moved.Detail is not null =>
                $"{_localizer.Error(error)}\n\n{moved.Detail}",
            _ => _localizer.Error(error),
        };
    }
}
