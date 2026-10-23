using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ordena.Backend.Data;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Repositories;
using Ordena.Backend.Services;

namespace Ordena.Backend;

public static class BackendServiceCollectionExtensions
{
    // The only wiring entry point for the Frontend. Everything is a singleton:
    // the services are stateless and open a short unit of work per call.
    // appDataFolder is for tests; the app uses %LocalAppData%\Ordena.
    public static IServiceCollection AddBackend(this IServiceCollection services, string? appDataFolder = null)
    {
        var paths = new AppPaths(appDataFolder ?? AppPaths.DefaultRoot);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = paths.Database }.ToString();

        // The Frontend's host adds Serilog on top; this makes ILogger<T> available without it, as in tests.
        services.AddLogging();

        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<SqliteBackup>();

        services.AddDbContextFactory<AppDbContext>(options => options
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaInterceptor()));
        services.AddSingleton<DatabaseMigrator>();
        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddSingleton<IFileStorage, FileStorage>();
        services.AddSingleton<PartnerResolver>();
        services.AddSingleton<ArchiveMover>();
        services.AddSingleton<JournalRecovery>();
        services.AddSingleton<ArchiveCheck>();
        services.AddSingleton<IndexRebuild>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPartnerService, PartnerService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IImportService, ImportService>();
        services.AddSingleton<IBulkImportService, BulkImportService>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<IMaintenanceService, MaintenanceService>();

        return services;
    }
}
