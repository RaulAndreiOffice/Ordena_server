using CommunityToolkit.Mvvm.ComponentModel;

namespace Ordena.Frontend.ViewModels;

public enum ImportState
{
    Pending,
    Saved,
    Skipped,
    Failed,
}

// One dropped PDF in the import queue. KeepSource: copied, not moved (see FilesDroppedMessage).
public sealed partial class ImportQueueItem(string path, bool keepSource = false) : ObservableObject
{
    public string Path { get; } = path;
    public string FileName { get; } = System.IO.Path.GetFileName(path);
    public bool KeepSource { get; } = keepSource;

    [ObservableProperty]
    private ImportState _state;

    // What happened to it: "Archivado como 2026/1T/…", or why it couldn't be imported.
    [ObservableProperty]
    private string? _message;

    // The file on screen.
    [ObservableProperty]
    private bool _isCurrent;
}
