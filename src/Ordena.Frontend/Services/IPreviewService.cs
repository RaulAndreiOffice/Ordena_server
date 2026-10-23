namespace Ordena.Frontend.Services;

// The PDF preview shows a temporary copy, so the viewer never keeps the original open:
// the original is moved into the archive while it is still on screen.
public interface IPreviewService
{
    // A copy in the app's preview folder, or null when the file can't be read (gone, locked).
    Task<string?> CreateCopyAsync(string path);

    // Deletes a copy made by CreateCopyAsync. Best effort: startup empties the folder anyway.
    void Release(string? copy);
}
