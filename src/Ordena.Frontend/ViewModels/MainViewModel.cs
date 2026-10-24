using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// The shell: the sidebar, the current page, the window title, the footer, and what is shown while the
// archive folder is missing.
public sealed partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IArchiveState _archive;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly IMessenger _messenger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowArchiveBanner), nameof(ShowArchivePanel))]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private string _windowTitle;

    public MainViewModel(
        INavigationService navigation, IArchiveState archive, IDialogService dialogs, ILocalizer localizer, IMessenger messenger)
    {
        _navigation = navigation;
        _archive = archive;
        _dialogs = dialogs;
        _localizer = localizer;
        _messenger = messenger;
        _windowTitle = localizer.Get("App_Title");
        archive.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ArchiveText));
            OnPropertyChanged(nameof(ArchiveUnavailableText));
            OnPropertyChanged(nameof(ShowArchiveBanner));
            OnPropertyChanged(nameof(ShowArchivePanel));
        };

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

    // "Carpeta de facturas: D:\Facturas" in the footer; it follows a move.
    public string ArchiveText => _localizer.Format("Footer_Archive", _archive.Root ?? "");

    // "La carpeta de facturas no está disponible: E:\Facturas", on the banner and the panel.
    public string ArchiveUnavailableText => _localizer.Format("Archive_Unavailable", _archive.Root ?? "");

    // While the folder is missing: a banner above every page, except where the panel says it already.
    public bool ShowArchiveBanner => !_archive.IsAvailable && CurrentPage?.NeedsArchive != true;

    // While the folder is missing: the panel instead of the pages that can't work without it (importing).
    public bool ShowArchivePanel => !_archive.IsAvailable && CurrentPage?.NeedsArchive == true;

    // Called once at startup, before the window shows: the Search page.
    public Task InitializeAsync() => _navigation.NavigateToAsync<SearchViewModel>();

    [RelayCommand]
    private Task NavigateAsync(NavItem item) => _navigation.NavigateToAsync(item.PageType);

    // "Reintentar": the disk may be plugged in again.
    [RelayCommand]
    private async Task RetryArchiveAsync()
    {
        await _archive.RefreshAsync();
        if (!_archive.IsAvailable)
            await _dialogs.ShowErrorAsync(_localizer.Format("Archive_StillUnavailable", _archive.Root ?? ""));
    }

    // "Buscar…": the folder was moved, and the user knows where.
    [RelayCommand]
    private Task LocateArchiveAsync() => _archive.LocateAsync();

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
        RefreshArchive();
    }

    // Each page change looks for the folder again, so a disk unplugged or plugged back in shows or hides
    // the banner. async void like an event handler, so an unexpected error reaches App's handler.
    private async void RefreshArchive() => await _archive.RefreshAsync();
}
