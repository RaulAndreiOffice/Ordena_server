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
}
