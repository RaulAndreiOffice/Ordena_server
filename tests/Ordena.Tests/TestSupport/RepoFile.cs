using System.Runtime.CompilerServices;

namespace Ordena.Tests.TestSupport;

// A file in the source tree, found by walking up to Ordena.slnx: from the test's output folder, or from
// this source file's own folder when the tests were built somewhere else (an OutDir outside the repo).
internal static class RepoFile
{
    public static string Path(params string[] parts)
    {
        var root = FindRoot(AppContext.BaseDirectory) ?? FindRoot(System.IO.Path.GetDirectoryName(SourceFile())!)
            ?? throw new InvalidOperationException("Ordena.slnx not found above the test output folder or the sources.");
        return System.IO.Path.Combine([root, .. parts]);
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;

    private static string? FindRoot(string start)
    {
        var folder = new DirectoryInfo(start);
        while (folder is not null && !File.Exists(System.IO.Path.Combine(folder.FullName, "Ordena.slnx")))
            folder = folder.Parent;
        return folder?.FullName;
    }
}
