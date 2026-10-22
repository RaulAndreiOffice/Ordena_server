namespace Ordena.Backend.Infrastructure;

internal sealed class AppPaths(string root) : IAppPaths
{
    // %LocalAppData%\Ordena
    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ordena");

    public string Root { get; } = root;
    public string Database => Path.Combine(Root, "ordena.db");
    public string Logs => Path.Combine(Root, "logs");
    public string Preview => Path.Combine(Root, "preview");
    public string WebView2 => Path.Combine(Root, "WebView2");
}
