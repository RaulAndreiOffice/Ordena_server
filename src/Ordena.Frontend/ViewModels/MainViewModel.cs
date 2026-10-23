using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Services;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// The shell: the sidebar, the current page, the window title and the footer.
public sealed partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly ILocalizer _localizer;
    private readonly IMessenger _messenger;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private string _windowTitle;

    // "Carpeta de facturas: D:\Facturas" in the footer.
    [ObservableProperty]
    private string _archiveText = "";

    public MainViewModel(
        INavigationService navigation, ISettingsService settings, ILocalizer localizer, IMessenger messenger)
    {
        _navigation = navigation;
        _settings = settings;
        _localizer = localizer;
        _messenger = messenger;
        _windowTitle = localizer.Get("App_Title");

        NavigationSections =
        [
            new NavSection(localizer.Get("Nav_Section_Documents"),
            [
                new NavItem(localizer.Get("Page_Search"), "Icon.Bold.Search", typeof(SearchViewModel)),
                new NavItem(localizer.Get("Page_Import"), "Icon.Bold.Import", typeof(ImportViewModel)),
                new NavItem(localizer.Get("Page_BulkImport"), "Icon.Bold.FolderOpen", typeof(BulkImportViewModel)),
                new NavItem(localizer.Get("Page_Trash"), "Icon.Bold.Trash", typeof(TrashViewModel)),
            ]),
            new NavSection(localizer.Get("Nav_Section_System"),
            [
                new NavItem(localizer.Get("Page_Settings"), "Icon.Bold.Settings", typeof(SettingsViewModel)),
            ]),
        ];

        var version = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(1, 0, 0);
        VersionText = localizer.Format("Footer_Version", version.ToString(3));

        // Selecting an entry navigates, whether by mouse, keyboard or accessibility tools.
        foreach (var item in NavigationSections.SelectMany(s => s.Items))
            item.PropertyChanged += OnNavItemChanged;
        navigation.CurrentPageChanged += (_, _) => OnCurrentPageChanged();
    }

    public IReadOnlyList<NavSection> NavigationSections { get; }

    public string VersionText { get; }

    // Called once at startup, before the window shows: the footer, then the Search page.
    public async Task InitializeAsync()
    {
        var settings = await _settings.GetAsync(CancellationToken.None);
        ArchiveText = _localizer.Format("Footer_Archive", settings.ArchiveRoot ?? "");
        await _navigation.NavigateToAsync<SearchViewModel>();
    }

    [RelayCommand]
    private Task NavigateAsync(NavItem item) => _navigation.NavigateToAsync(item.PageType);

    // PDFs dropped anywhere on the window go to the import queue. Import is shown first: that creates
    // the page if it hasn't been yet, so it is there to receive them.
    [RelayCommand]
    private async Task FilesDroppedAsync(string[] paths)
    {
        await _navigation.NavigateToAsync<ImportViewModel>();
        _messenger.Send(new FilesDroppedMessage(paths));
    }

    private void OnNavItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavItem.IsSelected)
            && sender is NavItem { IsSelected: true } item
            && item.PageType != CurrentPage?.GetType())
            NavigateCommand.Execute(item);
    }

    private void OnCurrentPageChanged()
    {
        CurrentPage = _navigation.CurrentPage;
        foreach (var item in NavigationSections.SelectMany(s => s.Items))
            item.IsSelected = item.PageType == CurrentPage?.GetType();

        var app = _localizer.Get("App_Title");
        WindowTitle = CurrentPage is null ? app : $"{app} — {CurrentPage.Title}";
    }
}
