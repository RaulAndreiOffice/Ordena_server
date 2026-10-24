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
    // Bumped by each refresh and by SetRoot. A refresh that finishes after a newer one started, or after the
    // folder was located, leaves what they found: "Reintentar" always looks again, and an old "missing"
    // can't bring the banner back over a folder just found.
    private int _version;

    [ObservableProperty]
    private string? _root;

    // Until the first refresh: startup has just looked, and settled the interrupted moves if it was there.
    [ObservableProperty]
    private bool _isAvailable = true;

    [ObservableProperty]
    private bool _isImporting;

    public async Task RefreshAsync()
    {
        var version = ++_version;
        var root = (await settings.GetAsync(CancellationToken.None)).ArchiveRoot;
        var available = await maintenance.GetArchiveStatusAsync(CancellationToken.None) != ArchiveStatus.Unavailable;
        if (version != _version)
            return;

        var cameBack = available && !IsAvailable;
        Root = root;
        IsAvailable = available;

        // Back after being away: settle what a crash left halfway meanwhile (only that: the backend leaves
        // moves in progress alone). After the banner has gone, so a recovery that fails can't keep it up.
        if (cameBack)
            await maintenance.RecoverInterruptedMovesAsync(CancellationToken.None);
    }

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
        _version++;
        Root = newRoot;
        IsAvailable = true;
    }
}
