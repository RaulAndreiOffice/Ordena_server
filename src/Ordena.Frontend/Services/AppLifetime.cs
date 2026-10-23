using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Ordena.Frontend.Services;

internal sealed class AppLifetime : IAppLifetime
{
    // Read by App.OnExit, which starts the new instance at the end of the shutdown.
    public bool RestartRequested { get; private set; }

    public void Restart()
    {
        RestartRequested = true;
        Application.Current.Shutdown();
    }

    // The published app is Ordena.exe itself. Under "dotnet run" or "dotnet Ordena.dll" the process
    // is dotnet.exe, which needs the dll again; without a console window of its own.
    public static void StartNewInstance()
    {
        var process = Environment.ProcessPath ?? throw new InvalidOperationException("The process has no path.");
        var start = new ProcessStartInfo(process) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(process), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(App).Assembly.Location);
        Process.Start(start);
    }
}
