using Ordena.Backend.Dtos;

namespace Ordena.Frontend.Services;

// Message boxes and dialogs, owned by the active window. ViewModels never touch MessageBox or Window directly.
public interface IDialogService
{
    // The edit dialog for one document, modal. True when it was saved.
    Task<bool> ShowEditDocumentAsync(int documentId);

    Task ShowErrorAsync(string message);

    Task ShowInfoAsync(string message);

    // Yes → true, No → false.
    Task<bool> ConfirmAsync(string message);

    // A question with the app's own buttons. The index of the one clicked (the first is the main one),
    // or -1 when the window was closed.
    Task<int> ChooseAsync(string message, params string[] choices);

    // A long job on the archive, behind a modal window with a progress bar and Cancelar, which cancels the
    // token. The window closes when the job ends; its result, or its exception (a cancel included), comes back.
    Task<T> RunWithProgressAsync<T>(string title, Func<IProgress<ArchiveProgress>, CancellationToken, Task<T>> work);
}
