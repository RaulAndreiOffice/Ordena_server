using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ordena.Backend.Dtos;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// First start: the language and the invoices folder, either a new one ("Empezar de cero") or one that
// already holds an archive ("Ya tengo un archivo de facturas": a new PC, or Windows reinstalled), whose
// index comes back from its newest backup, or else is rebuilt from its folders and file names.
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IArchiveService _archive;
    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;

    // Set once LoadAsync has picked the saved language: only later changes are the user's.
    private bool _loaded;

    [ObservableProperty]
    private LanguageOption _language;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _archiveRoot;

    // "Ya tengo un archivo de facturas" rather than "Empezar de cero".
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewArchive), nameof(FolderHint))]
    private bool _hasArchive;

    public FirstRunViewModel(
        ISettingsService settings, IArchiveService archive, IFilePickerService picker, IDialogService dialogs, ILocalizer localizer)
    {
        _settings = settings;
        _archive = archive;
        _picker = picker;
        _dialogs = dialogs;
        _localizer = localizer;

        Languages = LanguageOption.All(localizer);
        _language = Languages[0];
        _archiveRoot = settings.SuggestArchiveRoot();
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    // The other option button: "Empezar de cero".
    public bool IsNewArchive
    {
        get => !HasArchive;
        set => HasArchive = !value;
    }

    // Under the folder: a new one is created, an existing one is the one holding the years and _backup.
    public string FolderHint => _localizer.Get(HasArchive ? "FirstRun_ExistingFolderHint" : "FirstRun_FolderHint");

    // Raised once both values are saved; the window then closes.
    public event EventHandler? Completed;

    // Raised once another language is picked and saved; the window is then shown again in it.
    public event EventHandler? LanguageChanged;

    // Starts on the language already saved ("es" until one is picked). archiveRoot is the folder
    // chosen before a language change, kept when the window is shown again.
    public async Task LoadAsync(string? archiveRoot = null)
    {
        var current = (await _settings.GetAsync(CancellationToken.None)).Language;
        Language = Languages.FirstOrDefault(l => l.Code == current) ?? Languages[0];
        if (archiveRoot is not null)
            ArchiveRoot = archiveRoot;
        _loaded = true;
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        var folder = _picker.PickFolder(
            _localizer.Get(HasArchive ? "FirstRun_PickExistingTitle" : "FirstRun_PickFolderTitle"), ArchiveRoot);
        if (folder is not null)
            ArchiveRoot = folder;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        await _settings.SetLanguageAsync(Language.Code, CancellationToken.None);

        if (HasArchive)
        {
            if (!await OpenArchiveAsync())
                return;
        }
        else
        {
            var created = await _settings.SetArchiveRootAsync(ArchiveRoot, CancellationToken.None);
            if (!created.IsSuccess)
            {
                await _dialogs.ShowErrorAsync(_localizer.Format("FirstRun_CannotCreateFolder", ArchiveRoot));
                return;
            }
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }

    // The archive's newest backup, or with none, an index rebuilt from what its folders and file names say.
    // False to stay on first run: the folder is missing, or the user declined or cancelled the rebuild.
    private async Task<bool> OpenArchiveAsync()
    {
        var restored = await _archive.RestoreFromBackupAsync(ArchiveRoot, CancellationToken.None);
        if (restored.IsSuccess)
        {
            // The backup brings the settings of the PC it was made on; the language picked here wins.
            await _settings.SetLanguageAsync(Language.Code, CancellationToken.None);
            await _dialogs.ShowInfoAsync(_localizer.Format(
                "FirstRun_Restored", restored.Value.BackupTime, restored.Value.DocumentCount));
            return true;
        }
        if (restored.Error != ErrorCode.NoBackup)
        {
            await _dialogs.ShowErrorAsync(restored.Error == ErrorCode.ArchiveUnavailable
                ? _localizer.Format("FirstRun_FolderMissing", ArchiveRoot)
                : _localizer.Error(restored.Error.Value));
            return false;
        }

        if (!await _dialogs.ConfirmAsync(_localizer.Format("FirstRun_AskRebuild", ArchiveRoot)))
            return false;

        Result<RebuildReport> rebuilt;
        try
        {
            rebuilt = await _dialogs.RunWithProgressAsync(_localizer.Get("FirstRun_RebuildProgress"),
                (progress, ct) => _archive.RebuildIndexAsync(ArchiveRoot, progress, ct));
        }
        catch (OperationCanceledException)
        {
            return false; // the database is still empty
        }
        if (!rebuilt.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(_localizer.Error(rebuilt.Error.Value));
            return false;
        }

        var report = rebuilt.Value;
        var leftOut = report.Unrecognized.Count + report.Duplicates.Count;
        var message = _localizer.Format("FirstRun_Rebuilt", report.DocumentCount, report.PartnerCount);
        if (leftOut > 0)
            message += "\n\n" + _localizer.Format("FirstRun_RebuiltLeftOut", leftOut);
        await _dialogs.ShowInfoAsync(message);
        return true;
    }

    private bool CanStart() => !string.IsNullOrWhiteSpace(ArchiveRoot);

    partial void OnLanguageChanged(LanguageOption value)
    {
        if (_loaded)
            SwitchLanguage(value);
    }

    // Saved straight away: first run comes back in this language even if it is closed now.
    // async void like an event handler, so an unexpected error reaches App's handler.
    private async void SwitchLanguage(LanguageOption language)
    {
        await _settings.SetLanguageAsync(language.Code, CancellationToken.None);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
