using Ordena.Backend.Results;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

[Collection(CultureCollection.Name)]
public sealed class FirstRunViewModelTests : IDisposable
{
    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeSettingsService _settings = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FirstRunViewModel _firstRun;
    private int _completed;
    private int _languageChanged;

    public FirstRunViewModelTests()
    {
        _firstRun = new FirstRunViewModel(_settings, _picker, _dialogs, new Localizer());
        _firstRun.Completed += (_, _) => _completed++;
        _firstRun.LanguageChanged += (_, _) => _languageChanged++;
    }

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public void It_proposes_Spanish_and_Facturas_in_Documents()
    {
        Assert.Equal(["Español", "English"], _firstRun.Languages.Select(l => l.Name));
        Assert.Equal("es", _firstRun.Language.Code);
        Assert.Equal(@"C:\Users\Ana\Documents\Facturas", _firstRun.ArchiveRoot);
    }

    [Fact]
    public async Task It_starts_on_the_language_already_saved()
    {
        _settings.Language = "en";

        await _firstRun.LoadAsync();

        Assert.Equal("en", _firstRun.Language.Code);
    }

    // The window shows the choice by coming back in that language; saved first, so closing it then
    // still brings first run back in it.
    [Fact]
    public async Task Picking_another_language_saves_it_and_asks_for_the_window_in_it()
    {
        await _firstRun.LoadAsync();

        _firstRun.Language = _firstRun.Languages[1];

        Assert.Equal(["language en"], _settings.Saved);
        Assert.Equal(1, _languageChanged);
        Assert.Equal(0, _completed);
    }

    [Fact]
    public async Task Loading_the_saved_language_or_picking_it_again_is_not_a_change()
    {
        _settings.Language = "en";

        await _firstRun.LoadAsync();
        _firstRun.Language = _firstRun.Languages[1];

        Assert.Empty(_settings.Saved);
        Assert.Equal(0, _languageChanged);
    }

    [Fact]
    public async Task Shown_again_after_a_language_change_it_keeps_the_folder()
    {
        await _firstRun.LoadAsync(@"E:\Contabilidad\Facturas");

        Assert.Equal(@"E:\Contabilidad\Facturas", _firstRun.ArchiveRoot);
    }

    [Fact]
    public void Screen_readers_hear_each_language_by_its_name()
    {
        Assert.Equal(["Español", "English"], _firstRun.Languages.Select(l => l.ToString()));
    }

    [Fact]
    public void Change_opens_the_picker_at_the_current_folder_and_takes_the_choice()
    {
        _picker.Choice = @"E:\Contabilidad\Facturas";

        _firstRun.ChooseFolderCommand.Execute(null);

        Assert.Equal(@"C:\Users\Ana\Documents\Facturas", _picker.OpenedAt);
        Assert.Equal(@"E:\Contabilidad\Facturas", _firstRun.ArchiveRoot);
    }

    [Fact]
    public void A_cancelled_picker_keeps_the_folder()
    {
        _picker.Choice = null;

        _firstRun.ChooseFolderCommand.Execute(null);

        Assert.Equal(@"C:\Users\Ana\Documents\Facturas", _firstRun.ArchiveRoot);
    }

    [Fact]
    public async Task Start_saves_the_language_then_the_folder_and_completes()
    {
        _firstRun.Language = _firstRun.Languages[1];

        await _firstRun.StartCommand.ExecuteAsync(null);

        Assert.Equal(["language en", @"root C:\Users\Ana\Documents\Facturas"], _settings.Saved);
        Assert.Equal(1, _completed);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_created_shows_an_error_and_stays()
    {
        _settings.ArchiveRootResult = Result.Fail(ErrorCode.IoError, "drive missing");

        await _firstRun.StartCommand.ExecuteAsync(null);

        var error = Assert.Single(_dialogs.Errors);
        Assert.StartsWith("No se pudo crear la carpeta:", error);
        Assert.Contains(@"C:\Users\Ana\Documents\Facturas", error);
        Assert.Equal(0, _completed);
    }

    [Fact]
    public void Start_needs_a_folder()
    {
        _firstRun.ArchiveRoot = " ";

        Assert.False(_firstRun.StartCommand.CanExecute(null));
    }
}
