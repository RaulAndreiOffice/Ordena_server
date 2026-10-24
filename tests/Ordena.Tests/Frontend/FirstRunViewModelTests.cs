using Ordena.Backend.Dtos;
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
    private readonly FakeArchiveService _archive = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FirstRunViewModel _firstRun;
    private int _completed;
    private int _languageChanged;

    public FirstRunViewModelTests()
    {
        _firstRun = new FirstRunViewModel(_settings, _archive, _picker, _dialogs, new Localizer());
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

    [Fact]
    public void The_folder_hint_and_picker_follow_the_choice_between_a_new_archive_and_an_existing_one()
    {
        Assert.True(_firstRun.IsNewArchive);
        Assert.Equal("Se crea si todavía no existe.", _firstRun.FolderHint);
        var changed = new List<string?>();
        _firstRun.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _firstRun.IsNewArchive = false;

        Assert.True(_firstRun.HasArchive);
        Assert.Equal("La carpeta que contiene los años (2025, 2026…) y _backup.", _firstRun.FolderHint);
        Assert.Contains(nameof(_firstRun.FolderHint), changed);
    }

    // A new PC: the archive's newest backup gives every document back, with the language picked here
    // rather than the one saved in the backup.
    [Fact]
    public async Task An_existing_archive_comes_back_from_its_newest_backup()
    {
        _firstRun.HasArchive = true;
        _firstRun.ArchiveRoot = @"E:\Facturas";
        _archive.RestoreAnswer = _ =>
        {
            _settings.Language = "en"; // the old PC's
            return Result.Ok(new RestoreReport(@"E:\Facturas\_backup\ordena-20261023-1421.db", new DateTime(2026, 10, 23, 14, 21, 0), 3001));
        };

        await _firstRun.StartCommand.ExecuteAsync(null);

        Assert.Equal([@"restore E:\Facturas"], _archive.Calls);
        Assert.Equal("es", _settings.Language);
        Assert.Equal(["Recuperada la copia de seguridad del 23/10/2026 a las 14:21: 3.001 documentos."], _dialogs.Infos);
        Assert.DoesNotContain(_settings.Saved, s => s.StartsWith("root", StringComparison.Ordinal));
        Assert.Equal(1, _completed);
    }

    // No backup in the folder (it was never backed up, or _backup was lost): the names give the index back.
    [Fact]
    public async Task Without_a_backup_the_index_is_rebuilt_from_the_folders_and_file_names()
    {
        _firstRun.HasArchive = true;
        _firstRun.ArchiveRoot = @"E:\Facturas";
        _archive.RebuildAnswer = (_, progress, _) =>
        {
            progress?.Report(new ArchiveProgress(1, 1, 10, 10));
            return Result.Ok(new RebuildReport(2995, 41, [@"E:\Facturas\escaneo.pdf"], [@"E:\Facturas\copia.pdf"]));
        };

        await _firstRun.StartCommand.ExecuteAsync(null);

        Assert.Equal([@"restore E:\Facturas", @"rebuild E:\Facturas"], _archive.Calls);
        Assert.StartsWith("No hay ninguna copia de seguridad en esa carpeta:\nE:\\Facturas", Assert.Single(_dialogs.Questions));
        Assert.Equal(["Reconstruyendo el índice…"], _dialogs.ProgressTitles);
        Assert.Single(_dialogs.ProgressReports);
        Assert.Equal(["Índice reconstruido: 2.995 documentos de 41 proveedores y clientes.\n\n"
                      + "2 PDF no tienen el nombre que pone Ordena: los verás en Ajustes → Comprobar carpeta."], _dialogs.Infos);
        Assert.Equal(1, _completed);
    }

    [Fact]
    public async Task Declining_the_rebuild_stays_on_first_run()
    {
        _firstRun.HasArchive = true;
        _dialogs.Confirm = false;

        await _firstRun.StartCommand.ExecuteAsync(null);

        Assert.Single(_archive.Calls); // only the restore that found no backup
        Assert.Equal(0, _completed);
    }

    [Fact]
    public async Task A_cancelled_rebuild_stays_on_first_run()
    {
        _firstRun.HasArchive = true;
        _archive.RebuildAnswer = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("the rebuild should have been cancelled");
        };
        await _dialogs.CancelProgress.CancelAsync();

        await _firstRun.StartCommand.ExecuteAsync(null);

        Assert.Empty(_dialogs.Infos);
        Assert.Equal(0, _completed);
    }

    [Fact]
    public async Task An_existing_archive_in_a_folder_that_isnt_there_is_explained()
    {
        _firstRun.HasArchive = true;
        _firstRun.ArchiveRoot = @"E:\Facturas";
        _archive.RestoreAnswer = _ => Result.Fail<RestoreReport>(ErrorCode.ArchiveUnavailable, @"E:\Facturas");

        await _firstRun.StartCommand.ExecuteAsync(null);

        Assert.Equal(["Esa carpeta no existe:\nE:\\Facturas"], _dialogs.Errors);
        Assert.Equal(0, _completed);
    }

    [Fact]
    public void The_picker_for_an_existing_archive_asks_for_it_by_name()
    {
        _firstRun.HasArchive = true;
        _picker.Choice = @"E:\Facturas";

        _firstRun.ChooseFolderCommand.Execute(null);

        Assert.Equal("Elige tu carpeta de facturas", _picker.Title);
        Assert.Equal(@"E:\Facturas", _firstRun.ArchiveRoot);
    }
}
