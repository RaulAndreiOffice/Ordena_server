using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend;
using Ordena.Frontend;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// AddBackend + AddFrontend as App builds them: every registration resolves, windows included
// (validated, not created: windows need the UI thread).
public sealed class FrontendWiringTests : IDisposable
{
    private readonly TempFolder _appData = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _appData.Dispose();
    }

    [Fact]
    public void Every_registration_resolves_and_pages_have_the_right_lifetime()
    {
        using var provider = new ServiceCollection()
            .AddBackend(_appData.Root)
            .AddFrontend()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Search, Import and BulkImport keep their state between visits; the others start fresh each time.
        Assert.Same(provider.GetRequiredService<SearchViewModel>(), provider.GetRequiredService<SearchViewModel>());
        Assert.Same(provider.GetRequiredService<ImportViewModel>(), provider.GetRequiredService<ImportViewModel>());
        Assert.Same(provider.GetRequiredService<BulkImportViewModel>(), provider.GetRequiredService<BulkImportViewModel>());
        Assert.NotSame(provider.GetRequiredService<TrashViewModel>(), provider.GetRequiredService<TrashViewModel>());
        Assert.NotSame(provider.GetRequiredService<FirstRunViewModel>(), provider.GetRequiredService<FirstRunViewModel>());
        // Import's form is its own; the edit dialog will get another.
        Assert.NotSame(provider.GetRequiredService<DocumentFormViewModel>(), provider.GetRequiredService<ImportViewModel>().Form);
        Assert.NotNull(provider.GetRequiredService<MainViewModel>());
        // Each edit dialog gets its own ViewModel and form.
        Assert.NotSame(provider.GetRequiredService<EditDocumentViewModel>(), provider.GetRequiredService<EditDocumentViewModel>());
        // App.OnExit reads the restart request from the same instance Ajustes made it on.
        Assert.Same(provider.GetRequiredService<IAppLifetime>(), provider.GetRequiredService<IAppLifetime>());
    }
}
