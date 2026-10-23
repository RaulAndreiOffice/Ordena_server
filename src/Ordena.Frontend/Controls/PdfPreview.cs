using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Ordena.Frontend.Resources;

namespace Ordena.Frontend.Controls;

// Shows a PDF with Edge's built-in viewer (WebView2). Only the file is shown: no dev tools, no context
// menu, no saving the copy elsewhere, no following links out of it, and no files dropped onto it.
// Without the WebView2 Runtime it says so and offers OpenExternallyCommand ("Abrir en el visor").
public sealed class PdfPreview : ContentControl
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath), typeof(string), typeof(PdfPreview),
        new PropertyMetadata(null, (d, _) => ((PdfPreview)d).Show()));

    public static readonly DependencyProperty OpenExternallyCommandProperty = DependencyProperty.Register(
        nameof(OpenExternallyCommand), typeof(ICommand), typeof(PdfPreview));

    // One browser environment for the whole app, with its data in IAppPaths.WebView2 (set by App);
    // by default WebView2 would create its data folder next to the exe.
    private static Task<CoreWebView2Environment>? s_environment;

    private readonly WebView2 _webView = new() { AllowExternalDrop = false, Visibility = Visibility.Hidden };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly Button _openExternally;
    private readonly StackPanel _notice;
    private bool _ready;
    private bool _unavailable;
    private string? _shownUri;

    public PdfPreview()
    {
        _message.SetResourceReference(StyleProperty, "Text.Secondary");

        _openExternally = new Button { Content = Strings.Preview_OpenInViewer, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        _openExternally.SetResourceReference(StyleProperty, "Button.Secondary");
        _openExternally.SetBinding(Button.CommandProperty, new System.Windows.Data.Binding(nameof(OpenExternallyCommand)) { Source = this });

        _notice = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320 };
        _notice.Children.Add(_message);
        _notice.Children.Add(_openExternally);

        var grid = new Grid();
        grid.Children.Add(_webView);
        grid.Children.Add(_notice);
        Content = grid;

        Loaded += async (_, _) => await InitializeAsync();
    }

    public static string? UserDataFolder { get; set; }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public ICommand? OpenExternallyCommand
    {
        get => (ICommand?)GetValue(OpenExternallyCommandProperty);
        set => SetValue(OpenExternallyCommandProperty, value);
    }

    private async Task InitializeAsync()
    {
        if (_ready || _unavailable)
            return;

        try
        {
            // The PDF toolbar's own texts ("de 1", the tooltips) in the app's language.
            s_environment ??= CoreWebView2Environment.CreateAsync(null, UserDataFolder,
                new CoreWebView2EnvironmentOptions { Language = (Strings.Culture ?? CultureInfo.CurrentUICulture).Name });
            await _webView.EnsureCoreWebView2Async(await s_environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _unavailable = true;
            Show();
            return;
        }

        var core = _webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.Save | CoreWebView2PdfToolbarItems.SaveAs;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri != _shownUri && e.Uri != "about:blank")
                e.Cancel = true;
        };

        _ready = true;
        Show();
    }

    private void Show()
    {
        var path = FilePath;
        if (_unavailable)
        {
            _webView.Visibility = Visibility.Hidden;
            _message.Text = Strings.Preview_Unavailable;
            _openExternally.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
            _notice.Visibility = Visibility.Visible;
            return;
        }

        if (path is null)
        {
            _shownUri = null;
            _webView.Visibility = Visibility.Hidden;
            _message.Text = Strings.Preview_None;
            _notice.Visibility = Visibility.Visible;
            if (_ready)
                _webView.CoreWebView2.Navigate("about:blank");
            return;
        }

        _notice.Visibility = Visibility.Collapsed;
        if (!_ready)
            return; // shown once the viewer is ready

        _shownUri = new Uri(path).AbsoluteUri;
        _webView.Visibility = Visibility.Visible;
        _webView.CoreWebView2.Navigate(_shownUri);
    }
}
