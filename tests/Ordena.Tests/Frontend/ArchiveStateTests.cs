using Ordena.Backend.Dtos;
using Ordena.Backend.Results;
using Ordena.Frontend.Services;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// Where the archive is and whether it is there, as the screens see it, over fake services.
[Collection(CultureCollection.Name)]
public sealed class ArchiveStateTests : IDisposable
{
    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeMaintenanceService _maintenance = new();
    private readonly FakeArchiveService _archive = new();
    private readonly FakeSettingsService _settings = new() { ArchiveRoot = @"D:\Facturas" };
    private readonly FakeFilePicker _picker = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly ArchiveState _state;

    public ArchiveStateTests() =>
        _state = new ArchiveState(_maintenance, _archive, _settings, _picker, _dialogs, new Localizer());

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public async Task A_refresh_reads_where_the_folder_is_and_whether_it_is_there()
    {
        _maintenance.Status = ArchiveStatus.Unavailable;

        await _state.RefreshAsync();

        Assert.Equal((@"D:\Facturas", false), (_state.Root, _state.IsAvailable));
        Assert.DoesNotContain("recover", _maintenance.Calls);
    }

    // Startup settled them if the folder was there at the start; if it wasn't, they wait for it to come back.
    [Fact]
    public async Task Interrupted_moves_are_settled_once_when_the_folder_comes_back()
    {
        await _state.RefreshAsync();
        _maintenance.Status = ArchiveStatus.Unavailable;
        await _state.RefreshAsync();
        _maintenance.Status = ArchiveStatus.Available;
        await _state.RefreshAsync();
        await _state.RefreshAsync();

        Assert.Single(_maintenance.Calls, "recover");
        Assert.True(_state.IsAvailable);
    }

    // "Reintentar" while a page change's check is still under way (a slow network drive): it looks again,
    // and the older answer, from before the disk was back, doesn't count.
    [Fact]
    public async Task Retry_looks_again_and_an_older_check_finishing_late_doesnt_count()
    {
        _maintenance.Status = ArchiveStatus.Unavailable;
        await _state.RefreshAsync();
        var slow = _maintenance.HoldNextStatus = new TaskCompletionSource<ArchiveStatus>();
        var pageChange = _state.RefreshAsync();

        _maintenance.Status = ArchiveStatus.Available; // the disk is plugged in again
        await _state.RefreshAsync();
        slow.SetResult(ArchiveStatus.Unavailable);
        await pageChange;

        Assert.True(_state.IsAvailable);
        Assert.Single(_maintenance.Calls, "recover");
    }

    // A check still under way when the user finds the folder: its old "missing" mustn't bring the banner back.
    [Fact]
    public async Task A_check_finishing_after_the_folder_was_found_doesnt_undo_it()
    {
        var slow = _maintenance.HoldNextStatus = new TaskCompletionSource<ArchiveStatus>();
        var pageChange = _state.RefreshAsync();
        _picker.Choice = @"E:\Facturas";

        await _state.LocateAsync();
        slow.SetResult(ArchiveStatus.Unavailable);
        await pageChange;

        Assert.Equal((@"E:\Facturas", true), (_state.Root, _state.IsAvailable));
    }

    // The banner goes as soon as the folder is back; settling the interrupted moves comes after, so if it
    // fails, it fails once, and the folder stays usable.
    [Fact]
    public async Task A_recovery_that_fails_doesnt_keep_the_folder_unavailable()
    {
        _maintenance.Status = ArchiveStatus.Unavailable;
        await _state.RefreshAsync();
        _maintenance.Status = ArchiveStatus.Available;
        _maintenance.RecoverThrows = new InvalidOperationException("journal row points to no document");

        await Assert.ThrowsAsync<InvalidOperationException>(_state.RefreshAsync);
        await _state.RefreshAsync();

        Assert.True(_state.IsAvailable);
        Assert.Single(_maintenance.Calls, "recover");
    }

    [Fact]
    public async Task Find_switches_to_the_folder_that_holds_the_archive()
    {
        _maintenance.Status = ArchiveStatus.Unavailable;
        await _state.RefreshAsync();
        _picker.Choice = @"E:\Facturas";

        Assert.True(await _state.LocateAsync());

        Assert.Equal(@"D:\Facturas", _picker.OpenedAt);
        Assert.Equal([@"change E:\Facturas"], _archive.Calls);
        Assert.Equal((@"E:\Facturas", true), (_state.Root, _state.IsAvailable));
    }

    [Fact]
    public async Task Find_says_so_when_the_folder_doesnt_hold_this_archive()
    {
        await _state.RefreshAsync();
        _picker.Choice = @"E:\Otra";
        _archive.ChangeRootAnswer = _ => Result.Fail(ErrorCode.ArchiveMismatch, "found 0 of 20");

        Assert.False(await _state.LocateAsync());

        Assert.Equal(["Esa carpeta no contiene este archivo de facturas."], _dialogs.Errors);
        Assert.Equal(@"D:\Facturas", _state.Root);
    }

    [Fact]
    public async Task Find_cancelled_changes_nothing()
    {
        await _state.RefreshAsync();

        Assert.False(await _state.LocateAsync());

        Assert.Empty(_archive.Calls);
        Assert.Equal(@"D:\Facturas", _state.Root);
    }
}
