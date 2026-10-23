namespace Ordena.Backend.Infrastructure;

internal sealed class AppPaths(string root) : IAppPaths
{
    // Set to keep the app's data somewhere else: the dev seeder's demo data and the smoke tests,
    // which must never touch the user's own.
    public const string DataDirVariable = "ORDENA_DATA_DIR";

    // %LocalAppData%\Ordena, unless ORDENA_DATA_DIR says otherwise.
    public static string DefaultRoot { get; } = ResolveDefaultRoot(Environment.GetEnvironmentVariable);

    public static string ResolveDefaultRoot(Func<string, string?> environment) =>
        environment(DataDirVariable) is { Length: > 0 } folder
            ? Path.GetFullPath(folder)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ordena");

    public string Root { get; } = root;
    public string Database => Path.Combine(Root, "ordena.db");
    public string Logs => Path.Combine(Root, "logs");
    public string Preview => Path.Combine(Root, "preview");
    public string WebView2 => Path.Combine(Root, "WebView2");
}
