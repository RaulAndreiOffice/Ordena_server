namespace Ordena.Tests.TestSupport;

// A real folder on disk for file tests, deleted with everything in it at the end.
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Root);

    public string Root { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "Ordena.Tests", Guid.NewGuid().ToString("N"));

    public string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    // Creates the folders too. Returns the full path.
    public string WriteFile(string relativePath, byte[] content)
    {
        var path = Path(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    // Every file under the folder, relative and with "/", sorted, for exact asserts.
    public string[] Files() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
        .Select(f => System.IO.Path.GetRelativePath(Root, f).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}
