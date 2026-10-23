namespace Ordena.Frontend.Services;

// Windows' own file and folder pickers.
public interface IFilePickerService
{
    // The chosen folder, or null when the user cancels. Opens at initialFolder, or at its
    // nearest existing parent when that folder doesn't exist yet.
    string? PickFolder(string title, string? initialFolder);

    // The PDFs the user picked (several allowed), or none when they cancel.
    IReadOnlyList<string> PickPdfFiles(string title, string filterName);

    // One PDF, or null when the user cancels. Opens in initialFolder's nearest existing folder:
    // "Buscar archivo" starts where the missing file should be.
    string? PickPdfFile(string title, string filterName, string? initialFolder);
}
