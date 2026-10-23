using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ordena.Backend.Services;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// First start: the language and the invoices folder ("Empezar de cero").
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
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

    public FirstRunViewModel(
        ISettingsService settings, IFilePickerService picker, IDialogService dialogs, ILocalizer localizer)
    {
        _settings = settings;
        _picker = picker;
        _dialogs = dialogs;
        _localizer = localizer;

        Languages = LanguageOption.All(localizer);
        _language = Languages[0];
        _archiveRoot = settings.SuggestArchiveRoot();
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

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
        var folder = _picker.PickFolder(_localizer.Get("FirstRun_PickFolderTitle"), ArchiveRoot);
        if (folder is not null)
            ArchiveRoot = folder;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        await _settings.SetLanguageAsync(Language.Code, CancellationToken.None);

        var created = await _settings.SetArchiveRootAsync(ArchiveRoot, CancellationToken.None);
        if (!created.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(_localizer.Format("FirstRun_CannotCreateFolder", ArchiveRoot));
            return;
        }

        Completed?.Invoke(this, EventArgs.Empty);
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
