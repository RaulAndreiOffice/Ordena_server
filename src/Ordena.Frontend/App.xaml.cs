using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ordena.Backend;
using Ordena.Backend.Dtos;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Services;
using Ordena.Frontend.Controls;
using Ordena.Frontend.Resources;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Frontend.Views.FirstRun;
using Ordena.Frontend.Views.Shell;
using Serilog;
using Serilog.Events;

namespace Ordena.Frontend;

// The composition root and the startup sequence (Plan.md, "Startup sequence").
public partial class App : Application
{
    private IHost? _host;
    private ILogger<App>? _logger;
    private SingleInstance? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Until the main window is up, closing another window (first run) must not end the app on its own.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        RegisterExceptionHandlers();

        try
        {
            if (!await StartAsync())
                Shutdown();
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // "Reiniciar ahora" in Ajustes: the new instance starts once this one has finished.
            var restart = _host.Services.GetService<IAppLifetime>() is AppLifetime { RestartRequested: true };

            try
            {
                // Waited for: it runs on the thread pool and takes well under a second.
                var backup = _host.Services.GetRequiredService<IMaintenanceService>()
                    .BackupIfChangedAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (!backup.IsSuccess)
                    _logger?.LogWarning("No backup on exit: {Error} {Detail}", backup.Error, backup.Detail);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "The backup on exit failed");
            }

            _host.StopAsync().GetAwaiter().GetResult();

            // Let go first, or the new instance would find this one still running and hand over to it.
            _instance?.Dispose();
            if (restart)
                StartNewInstance();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    // False when the user closed first run without finishing it: the app ends there.
    private async Task<bool> StartAsync()
    {
        _host = BuildHost();

        // Already open on this data: that window comes to the front, and this start ends here, before
        // touching the database. Taken on the UI thread, which also lets go of it in OnExit.
        _instance = SingleInstance.TryAcquire(
            _host.Services.GetRequiredService<IAppPaths>().Root, () => Dispatcher.InvokeAsync(BringToFront));
        if (_instance is null)
        {
            _host.Dispose();
            _host = null; // nothing to back up on exit
            return false;
        }

        await _host.StartAsync();
        var services = _host.Services;
        _logger = services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("Ordena {Version} starting, data in {Root}",
            typeof(App).Assembly.GetName().Version, services.GetRequiredService<IAppPaths>().Root);

        // Before reading any setting: on the very first start this is what creates the database.
        await services.GetRequiredService<IMaintenanceService>().RunStartupAsync(CancellationToken.None);

        var settingsService = services.GetRequiredService<ISettingsService>();
        var settings = await settingsService.GetAsync(CancellationToken.None);
        var culture = ApplyCulture(settings.Language);

        // Without this, bindings format dates and numbers as en-US whatever the culture.
        // It can be set only once per process, so it comes before the first window.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        if (settings.ArchiveRoot is null)
        {
            if (!await RunFirstRunAsync(services))
                return false;

            settings = await settingsService.GetAsync(CancellationToken.None);
            culture = ApplyCulture(settings.Language);
        }

        await WaitForArchiveAsync(services);

        // The preview's browser profile lives with the app's data, not next to the executable.
        PdfPreview.UserDataFolder = services.GetRequiredService<IAppPaths>().WebView2;

        var mainWindow = services.GetRequiredService<MainWindow>();
        // The language picked in first run applies to this session's bindings too.
        mainWindow.Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        await services.GetRequiredService<MainViewModel>().InitializeAsync();

        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();

        _ = WarmUpAsync(services.GetRequiredService<IMaintenanceService>());
        return true;
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddBackend();
        builder.Services.AddFrontend();
        builder.Services.AddSerilog((services, logger) => logger
            .MinimumLevel.Information()
            // EF Core logs every SQL command at Information.
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(services.GetRequiredService<IAppPaths>().Logs, "ordena-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30));
        return builder.Build();
    }

    // "es" → es-ES and "en" → en-GB: English with day/month/year dates, as used in Spain.
    // The app's texts take it from Strings.Culture, which holds for the whole process. WPF's own texts
    // (the date picker's placeholder, the text boxes' menu) take the UI thread's culture, and one set
    // inside this async startup reverts to Windows' own language once the startup returns. So it is
    // also set from a dispatcher operation that doesn't flow the startup's context: that one stays.
    private CultureInfo ApplyCulture(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language == UserSettings.English ? "en-GB" : "es-ES");
        Strings.Culture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        using (ExecutionContext.SuppressFlow())
        {
            Dispatcher.InvokeAsync(() =>
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            });
        }
        return culture;
    }

    // Picking another language closes first run; it is shown again in that language, a new window
    // because its texts are read when it loads, with the folder already chosen kept.
    private async Task<bool> RunFirstRunAsync(IServiceProvider services)
    {
        string? folder = null;
        while (true)
        {
            var window = services.GetRequiredService<FirstRunWindow>();
            window.Language = XmlLanguage.GetLanguage((Strings.Culture ?? CultureInfo.CurrentCulture).IetfLanguageTag);
            await window.ViewModel.LoadAsync(folder);
            if (window.ShowDialog() == true)
                return true;
            if (!window.SwitchingLanguage)
                return false;

            folder = window.ViewModel.ArchiveRoot;
            var settings = await services.GetRequiredService<ISettingsService>().GetAsync(CancellationToken.None);
            ApplyCulture(settings.Language);
        }
    }

    private void StartNewInstance()
    {
        try
        {
            AppLifetime.StartNewInstance();
            _logger?.LogInformation("Restarting");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger?.LogError(ex, "Could not start the new instance");
        }
    }

    // "La carpeta de facturas no está disponible": try again, find it where it was moved, or go on without it.
    // Without it, search works and the shell's banner offers the same until the folder is back.
    private static async Task WaitForArchiveAsync(IServiceProvider services)
    {
        var archive = services.GetRequiredService<IArchiveState>();
        await archive.RefreshAsync();
        if (archive.IsAvailable)
            return;

        var dialogs = services.GetRequiredService<IDialogService>();
        var localizer = services.GetRequiredService<ILocalizer>();
        while (!archive.IsAvailable)
        {
            var choice = await dialogs.ChooseAsync(localizer.Format("Startup_ArchiveUnavailable", archive.Root),
                localizer.Get("Archive_Retry"), localizer.Get("Archive_Locate"), localizer.Get("Archive_Continue"));
            if (choice == 0)
                await archive.RefreshAsync();
            else if (choice == 1)
                await archive.LocateAsync();
            else
                return; // Continuar, or the window closed
        }
    }

    // Ordena was started again: the window already open comes back instead, the main one or first run.
    private void BringToFront()
    {
        if (Windows.OfType<Window>().LastOrDefault(w => w.IsVisible) is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private async Task WarmUpAsync(IMaintenanceService maintenance)
    {
        try
        {
            await maintenance.WarmUpAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "The warm-up queries failed");
        }
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            ReportUnexpected(e.Exception);
            e.Handled = true; // the app keeps running; the details are in the log
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception; the app is closing");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger?.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    private void ReportUnexpected(Exception ex)
    {
        _logger?.LogError(ex, "Unexpected error");
        var logs = _host?.Services.GetService<IAppPaths>()?.Logs ?? "";
        MessageBox.Show(
            string.Format(CultureInfo.CurrentCulture, Strings.Error_Unexpected, logs),
            Strings.Dialog_Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
