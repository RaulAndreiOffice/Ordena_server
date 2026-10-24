using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Frontend.Views.FirstRun;
using Ordena.Frontend.Views.Shared;
using Ordena.Frontend.Views.Shell;

namespace Ordena.Frontend;

public static class FrontendServiceCollectionExtensions
{
    // Lifetimes from Plan.md: singletons for what lives as long as the app (the shell, and the Search,
    // Import and Importar carpeta pages, which keep their query, queue and run between visits);
    // transient for the rest.
    public static IServiceCollection AddFrontend(this IServiceCollection services)
    {
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<ILocalizer, Localizer>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IPreviewService, PreviewService>();
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IAppLifetime, AppLifetime>();
        services.AddSingleton<IArchiveState, ArchiveState>();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ImportViewModel>();
        services.AddSingleton<BulkImportViewModel>();
        services.AddTransient<TrashViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ArchiveSettingsViewModel>();
        services.AddTransient<DocumentFormViewModel>();

        services.AddTransient<FirstRunWindow>();
        services.AddTransient<FirstRunViewModel>();
        services.AddTransient<EditDocumentWindow>();
        services.AddTransient<EditDocumentViewModel>();

        return services;
    }
}
