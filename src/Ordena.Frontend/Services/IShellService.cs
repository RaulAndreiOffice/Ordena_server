namespace Ordena.Frontend.Services;

// What Windows itself opens: files in their default program, folders in Explorer.
public interface IShellService
{
    // A PDF in the default viewer.
    void OpenFile(string path);

    // Explorer, in the file's folder with the file selected.
    void ShowInFolder(string path);
}
