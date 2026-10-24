using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend.Dtos;
using Ordena.Frontend.Views.Shared;

namespace Ordena.Frontend.Services;

// The standard Windows message box for now; a dialog in the app's own style can replace it later
// without touching the ViewModels.
internal sealed class DialogService(ILocalizer localizer, IServiceProvider services) : IDialogService
{
    public async Task<bool> ShowEditDocumentAsync(int documentId)
    {
        var window = services.GetRequiredService<EditDocumentWindow>();
        window.Owner = OwnerWindow();
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

    public Task<int> ChooseAsync(string message, params string[] choices)
    {
        var window = new ChoiceWindow(message, choices);
        SetOwner(window);
        window.ShowDialog();
        return Task.FromResult(window.Choice);
    }

    public async Task<T> RunWithProgressAsync<T>(
        string title, Func<IProgress<ArchiveProgress>, CancellationToken, Task<T>> work)
    {
        var window = new ProgressWindow(title);
        SetOwner(window);
        using var cancel = new CancellationTokenSource();
        window.CancelRequested += (_, _) => cancel.Cancel();

        // Created here, on the UI thread, so the reports arrive there.
        var progress = new Progress<ArchiveProgress>(window.Report);

        // The job starts once the window shows, and closes it when it ends, which ends ShowDialog.
        Task<T>? job = null;
        window.ContentRendered += (_, _) => job ??= RunAsync();
        window.ShowDialog();
        return await job!;

        async Task<T> RunAsync()
        {
            try
            {
                return await work(progress, cancel.Token);
            }
            finally
            {
                _ = window.Dispatcher.InvokeAsync(window.Finish);
            }
        }
    }

    private MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var caption = localizer.Get("Dialog_Title");
        var owner = OwnerWindow();
        return owner is null
            ? MessageBox.Show(message, caption, buttons, image)
            : MessageBox.Show(owner, message, caption, buttons, image);
    }

    // Before the main window exists (startup's "no disponible"), a dialog stands alone, centered and in the taskbar.
    private static void SetOwner(Window window)
    {
        var owner = OwnerWindow();
        if (owner is not null)
        {
            window.Owner = owner;
            return;
        }
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowInTaskbar = true;
    }

    // The active window, or else the app's last one shown: a dialog stays with the app even while the user
    // is working in another program.
    private static Window? OwnerWindow()
    {
        var windows = Application.Current?.Windows.OfType<Window>().ToList() ?? [];
        return windows.FirstOrDefault(w => w.IsActive) ?? windows.LastOrDefault(w => w.IsVisible);
    }
}
