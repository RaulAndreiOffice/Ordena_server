using System.Xml.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend.Services;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// The shell's ViewModel with the real navigation, pages and texts; the services behind them are faked.
[Collection(CultureCollection.Name)]
public sealed class MainViewModelTests : IDisposable
{
    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeSettingsService _settings = new() { ArchiveRoot = @"D:\Facturas" };
    private readonly FakeImportService _import = new();
    private readonly MainViewModel _main;

    public MainViewModelTests()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMessenger>(new WeakReferenceMessenger())
            .AddSingleton<ILocalizer, Localizer>()
            .AddSingleton<INavigationService, NavigationService>()
            .AddSingleton<IImportService>(_import)
            .AddSingleton<ISearchService>(new FakeSearchService())
            .AddSingleton<IDocumentService, FakeDocumentService>()
            .AddSingleton<IPartnerService>(new FakePartnerService())
            .AddSingleton<IPreviewService, FakePreview>()
            .AddSingleton<IFilePickerService, FakeFilePicker>()
            .AddSingleton<IDialogService, FakeDialogs>()
            .AddSingleton<IShellService, FakeShell>()
            .AddSingleton<ISettingsService>(_settings)
            .AddSingleton<IAppLifetime, FakeAppLifetime>()
            .AddSingleton<IBulkImportService, FakeBulkImportService>()
            .AddSingleton<SearchViewModel>()
            .AddSingleton<ImportViewModel>()
            .AddSingleton<BulkImportViewModel>()
            .AddTransient<DocumentFormViewModel>()
            .AddTransient<TrashViewModel>()
            .AddTransient<SettingsViewModel>()
            .BuildServiceProvider();
        _main = new MainViewModel(
            services.GetRequiredService<INavigationService>(), _settings, services.GetRequiredService<ILocalizer>(),
            services.GetRequiredService<IMessenger>());
    }

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public void The_sidebar_has_the_documents_pages_then_the_system_ones()
    {
        Assert.Equal(["Documentos", "Sistema"], _main.NavigationSections.Select(s => s.Title));
        Assert.Equal(["Buscar", "Importar", "Importar carpeta", "Papelera"], _main.NavigationSections[0].Items.Select(i => i.Title));
        Assert.Equal(["Ajustes"], _main.NavigationSections[1].Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Startup_shows_the_search_page_and_the_archive_folder()
    {
        await _main.InitializeAsync();

        Assert.IsType<SearchViewModel>(_main.CurrentPage);
        Assert.Equal("Ordena — Buscar", _main.WindowTitle);
        Assert.Equal(@"Carpeta de facturas: D:\Facturas", _main.ArchiveText);
        Assert.Equal([true, false, false, false, false], AllItems().Select(i => i.IsSelected));
    }

    [Fact]
    public async Task Choosing_an_entry_shows_its_page_and_moves_the_selection()
    {
        await _main.InitializeAsync();
        var trash = _main.NavigationSections[0].Items[3];

        await _main.NavigateCommand.ExecuteAsync(trash);

        Assert.IsType<TrashViewModel>(_main.CurrentPage);
        Assert.Equal("Ordena — Papelera", _main.WindowTitle);
        Assert.Equal([false, false, false, true, false], AllItems().Select(i => i.IsSelected));
    }

    // Keyboard and accessibility tools check an entry without clicking it; that must navigate too.
    [Fact]
    public async Task Selecting_an_entry_without_a_click_shows_its_page()
    {
        await _main.InitializeAsync();
        var settings = _main.NavigationSections[1].Items[0];

        settings.IsSelected = true;

        Assert.IsType<SettingsViewModel>(_main.CurrentPage);
        Assert.Equal([false, false, false, false, true], AllItems().Select(i => i.IsSelected));
    }

    // From any page: Import comes up with the dropped files queued, the first one on screen.
    [Fact]
    public async Task Dropped_pdfs_show_the_import_page_with_them_queued()
    {
        await _main.InitializeAsync();

        await _main.FilesDroppedCommand.ExecuteAsync(new[] { @"C:\Descargas\a.pdf", @"C:\Descargas\b.pdf" });

        var import = Assert.IsType<ImportViewModel>(_main.CurrentPage);
        Assert.Equal("Ordena — Importar", _main.WindowTitle);
        Assert.Equal([false, true, false, false, false], AllItems().Select(i => i.IsSelected));
        Assert.Equal(["a.pdf", "b.pdf"], import.Queue.Select(i => i.FileName));
        Assert.Equal(@"C:\Descargas\a.pdf", import.CurrentItem?.Path);
        Assert.Equal([@"C:\Descargas\a.pdf"], _import.Checked);
    }

    // The sidebar names its icons by key; each must exist in Icons.xaml and be a valid geometry.
    [Fact]
    public void Every_sidebar_icon_is_a_geometry_in_Icons_xaml()
    {
        var icons = XDocument.Load(RepoFile.Path("src", "Ordena.Frontend", "Resources", "Theme", "Icons.xaml"))
            .Descendants()
            .Where(e => e.Name.LocalName == "Geometry")
            .ToDictionary(e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!, e => e.Value);

        foreach (var item in AllItems())
            Assert.True(icons.ContainsKey(item.IconKey), $"{item.IconKey} is not in Icons.xaml");
        foreach (var (key, data) in icons)
            Assert.False(Geometry.Parse(data).IsEmpty(), $"{key} parses to an empty geometry");
    }

    private IEnumerable<NavItem> AllItems() => _main.NavigationSections.SelectMany(s => s.Items);
}
