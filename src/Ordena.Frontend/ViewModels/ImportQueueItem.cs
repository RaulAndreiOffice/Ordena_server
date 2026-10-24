using CommunityToolkit.Mvvm.ComponentModel;

namespace Ordena.Frontend.ViewModels;

public enum ImportState
{
    Pending,
    Saved,
    Skipped,
    Failed,
}

// One dropped PDF in the import queue. KeepSource: copied, not moved; StartsPaid: its form starts
// ticked as paid (see FilesDroppedMessage).
public sealed partial class ImportQueueItem(string path, bool keepSource = false, bool startsPaid = false) : ObservableObject
{
    public string Path { get; } = path;
    public string FileName { get; } = System.IO.Path.GetFileName(path);
    public bool KeepSource { get; } = keepSource;
    public bool StartsPaid { get; } = startsPaid;

    [ObservableProperty]
    private ImportState _state;

    // What happened to it: "Archivado como 2026/1T/…", or why it couldn't be imported.
    [ObservableProperty]
    private string? _message;

    // The file on screen.
    [ObservableProperty]
    private bool _isCurrent;
}
