using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Frontend.Views.Shared;

namespace Ordena.Frontend.Services;

// The standard Windows message box for now; a dialog in the app's own style can replace it later
// without touching the ViewModels.
internal sealed class DialogService(ILocalizer localizer, IServiceProvider services) : IDialogService
{
    public async Task<bool> ShowEditDocumentAsync(int documentId)
    {
        var window = services.GetRequiredService<EditDocumentWindow>();
        window.Owner = ActiveWindow();
        await window.ViewModel.LoadAsync(documentId);
        return window.ShowDialog() == true;
    }

    public Task ShowErrorAsync(string message)
    {
        Show(message, MessageBoxButton.OK, MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string message)
    {
        Show(message, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string message) =>
        Task.FromResult(Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

    private MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var caption = localizer.Get("Dialog_Title");
        var owner = ActiveWindow();
        return owner is null
            ? MessageBox.Show(message, caption, buttons, image)
            : MessageBox.Show(owner, message, caption, buttons, image);
    }

    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
}
