using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Ordena.Frontend.Services;

internal sealed class FilePickerService : IFilePickerService
{
    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = NearestExisting(initialFolder) };
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    public IReadOnlyList<string> PickPdfFiles(string title, string filterName)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = $"{filterName} (*.pdf)|*.pdf", Multiselect = true };
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }

    public string? PickPdfFile(string title, string filterName, string? initialFolder)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{filterName} (*.pdf)|*.pdf",
            InitialDirectory = NearestExisting(initialFolder),
        };
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    // The suggested folder (Documents\Facturas) usually doesn't exist yet: open its parent instead.
    private static string? NearestExisting(string? folder)
    {
        while (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            folder = Path.GetDirectoryName(folder);
        return string.IsNullOrEmpty(folder) ? null : folder;
    }
}
