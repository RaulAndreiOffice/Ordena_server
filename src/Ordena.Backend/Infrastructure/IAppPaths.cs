namespace Ordena.Backend.Infrastructure;

// Where the app keeps its own data, per user and never next to the exe.
// Public and read-only because the Frontend needs the preview and WebView2 folders.
public interface IAppPaths
{
    string Root { get; }
    string Database { get; }
    string Logs { get; }
    string Preview { get; }
    string WebView2 { get; }
}
