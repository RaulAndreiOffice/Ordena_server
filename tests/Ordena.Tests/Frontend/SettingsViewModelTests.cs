using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// Ajustes' language: saved when picked, with a restart offered while it isn't the one on screen.
// The app runs in Spanish here.
[Collection(CultureCollection.Name)]
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeSettingsService _settings = new() { ArchiveRoot = @"D:\Facturas" };
    private readonly FakeAppLifetime _lifetime = new();

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public async Task It_shows_the_saved_language_without_a_restart()
    {
        var page = await OpenAsync();

        Assert.Equal("Ajustes", page.Title);
        Assert.Equal(["Español", "English"], page.Languages.Select(l => l.Name));
        Assert.Equal("es", page.SelectedLanguage.Code);
        Assert.False(page.RestartNeeded);
        Assert.Empty(_settings.Saved);
    }

    [Fact]
    public async Task Picking_English_saves_it_and_offers_the_restart()
    {
        var page = await OpenAsync();
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        page.SelectedLanguage = page.Languages[1];

        Assert.Equal(["language en"], _settings.Saved);
        Assert.True(page.RestartNeeded);
        Assert.Contains(nameof(page.RestartNeeded), changed);
    }

    [Fact]
    public async Task Going_back_to_the_language_on_screen_saves_it_and_hides_the_restart()
    {
        var page = await OpenAsync();

        page.SelectedLanguage = page.Languages[1];
        page.SelectedLanguage = page.Languages[0];

        Assert.Equal(["language en", "language es"], _settings.Saved);
        Assert.False(page.RestartNeeded);
    }

    // Picked earlier and not restarted yet: the page, opened again, still offers it.
    [Fact]
    public async Task A_language_saved_earlier_still_offers_the_restart()
    {
        _settings.Language = "en";

        var page = await OpenAsync();

        Assert.Equal("en", page.SelectedLanguage.Code);
        Assert.True(page.RestartNeeded);
        Assert.Empty(_settings.Saved);
    }

    [Fact]
    public async Task Restart_now_restarts_the_app()
    {
        var page = await OpenAsync();
        page.SelectedLanguage = page.Languages[1];

        await page.RestartCommand.ExecuteAsync(null);

        Assert.Equal(1, _lifetime.Restarts);
    }

    private async Task<SettingsViewModel> OpenAsync()
    {
        var page = new SettingsViewModel(_settings, new Localizer(), _lifetime);
        await page.OnNavigatedToAsync();
        return page;
    }
}
