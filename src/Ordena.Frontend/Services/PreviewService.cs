using System.IO;
using Ordena.Backend.Infrastructure;

namespace Ordena.Frontend.Services;

internal sealed class PreviewService(IAppPaths paths) : IPreviewService
{
    public Task<string?> CreateCopyAsync(string path) => Task.Run<string?>(() =>
    {
        try
        {
            Directory.CreateDirectory(paths.Preview);
            var copy = Path.Combine(paths.Preview, $"{Guid.NewGuid():N}.pdf");
            File.Copy(path, copy);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    });

    public void Release(string? copy)
    {
        if (copy is null)
            return;
        try
        {
            File.Delete(copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still shown by the viewer; startup empties the folder.
        }
    }
}
