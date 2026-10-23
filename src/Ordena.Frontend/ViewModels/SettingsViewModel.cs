using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ordena.Backend.Services;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// Ajustes. For now the language: saved as soon as it is picked, shown after a restart, because the
// texts are read once per run.
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly IAppLifetime _lifetime;

    // Set once the saved language is shown: only later changes are the user's.
    private bool _loaded;

    // The last language saved; finished before restarting.
    private Task _saving = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestartNeeded))]
    private LanguageOption _selectedLanguage;

    public SettingsViewModel(ISettingsService settings, ILocalizer localizer, IAppLifetime lifetime)
    {
        _settings = settings;
        _localizer = localizer;
        _lifetime = lifetime;
        Languages = LanguageOption.All(localizer);
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == localizer.Language) ?? Languages[0];
    }

    public override string Title => _localizer.Get("Page_Settings");

    public IReadOnlyList<LanguageOption> Languages { get; }

    // The saved language isn't the one on screen: "Reiniciar ahora" is offered.
    public bool RestartNeeded => SelectedLanguage.Code != _localizer.Language;

    public override async Task OnNavigatedToAsync()
    {
        var saved = (await _settings.GetAsync(CancellationToken.None)).Language;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == saved) ?? Languages[0];
        _loaded = true;
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (_loaded)
            _saving = _settings.SetLanguageAsync(value.Code, CancellationToken.None);
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        await _saving;
        _lifetime.Restart();
    }
}
