using CommunityToolkit.Mvvm.ComponentModel;
using Ordena.Backend.Dtos;
using Ordena.Backend.Services;

namespace Ordena.Frontend.Services;

internal sealed partial class ArchiveState(
    IMaintenanceService maintenance,
    IArchiveService archive,
    ISettingsService settings,
    IFilePickerService picker,
    IDialogService dialogs,
    ILocalizer localizer) : ObservableObject, IArchiveState
{
    // The refresh under way: page changes in a row share it.
    private Task _refreshing = Task.CompletedTask;

    [ObservableProperty]
    private string? _root;

    // Until the first refresh: startup has just looked, and settled the interrupted moves if it was there.
    [ObservableProperty]
    private bool _isAvailable = true;

    [ObservableProperty]
    private bool _isImporting;

    public Task RefreshAsync() => _refreshing.IsCompleted ? _refreshing = RefreshCoreAsync() : _refreshing;

    public async Task<bool> LocateAsync()
    {
        var folder = picker.PickFolder(localizer.Get("Archive_LocateTitle"), Root);
        if (folder is null)
            return false;

        // It also settles the interrupted moves there.
        var changed = await archive.ChangeRootAsync(folder, CancellationToken.None);
        if (!changed.IsSuccess)
        {
            await dialogs.ShowErrorAsync(localizer.Error(changed.Error.Value));
            return false;
        }

        SetRoot(folder);
        return true;
    }

    public void SetRoot(string newRoot)
    {
        Root = newRoot;
        IsAvailable = true;
    }

    private async Task RefreshCoreAsync()
    {
        Root = (await settings.GetAsync(CancellationToken.None)).ArchiveRoot;
        var available = await maintenance.GetArchiveStatusAsync(CancellationToken.None) != ArchiveStatus.Unavailable;
        if (available && !IsAvailable)
            await maintenance.RecoverInterruptedMovesAsync(CancellationToken.None);
        IsAvailable = available;
    }
}
