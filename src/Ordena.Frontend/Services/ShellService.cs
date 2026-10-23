using System.Diagnostics;

namespace Ordena.Frontend.Services;

internal sealed class ShellService : IShellService
{
    public void OpenFile(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public void ShowInFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path } })?.Dispose();
}
