using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Services;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly TempFolder _folder = new();
    private readonly SettingsService _settings;

    public SettingsServiceTests() =>
        _settings = new SettingsService(_db.UnitOfWorkFactory, new FileStorage(TimeSpan.FromMilliseconds(1)));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _folder.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task Defaults_before_first_run()
    {
        var settings = await _settings.GetAsync(Ct);

        Assert.Equal(new UserSettings(null, UserSettings.Spanish, null), settings);
    }

    [Fact]
    public async Task Saved_values_are_read_back_and_updates_replace_them()
    {
        await _settings.SetArchiveRootAsync(_folder.Path("Facturas"), Ct);
        await _settings.SetArchiveRootAsync(_folder.Path("Facturas-nuevas"), Ct);
        await _settings.SetLanguageAsync(UserSettings.English, Ct);

        var settings = await _settings.GetAsync(Ct);

        Assert.Equal(_folder.Path("Facturas-nuevas"), settings.ArchiveRoot);
        Assert.Equal("en", settings.Language);
        Assert.Equal(2L, _db.Scalar("SELECT COUNT(*) FROM Settings"));
    }

    [Fact]
    public async Task LastBackup_is_read_as_utc()
    {
        _db.Save(new AppSetting { Key = "LastBackup", Value = "2026-10-01T08:00:00.0000000Z" });

        var lastBackup = (await _settings.GetAsync(Ct)).LastBackupUtc;

        Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), lastBackup);
        Assert.Equal(DateTimeKind.Utc, lastBackup?.Kind);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("ES")]
    [InlineData("")]
    public async Task An_unsupported_language_throws(string language)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _settings.SetLanguageAsync(language, Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Facturas")]
    [InlineData(@"..\Facturas")]
    public async Task A_relative_archive_root_throws(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _settings.SetArchiveRootAsync(path, Ct));
    }

    [Fact]
    public async Task The_archive_folder_is_created_when_it_does_not_exist_yet()
    {
        var root = _folder.Path("Documentos", "Facturas");

        var result = await _settings.SetArchiveRootAsync(root, Ct);

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(root));
        Assert.Equal(root, (await _settings.GetAsync(Ct)).ArchiveRoot);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_created_is_IoError_and_not_saved()
    {
        _folder.WriteFile("blocker", [1]); // a file where a folder should go

        var result = await _settings.SetArchiveRootAsync(_folder.Path("blocker", "Facturas"), Ct);

        Assert.Equal(ErrorCode.IoError, result.Error);
        Assert.Null((await _settings.GetAsync(Ct)).ArchiveRoot);
    }

    [Fact]
    public void The_suggested_archive_root_is_Facturas_in_Documents()
    {
        var suggestion = _settings.SuggestArchiveRoot();

        Assert.True(Path.IsPathFullyQualified(suggestion));
        Assert.Equal("Facturas", Path.GetFileName(suggestion));
    }
}
