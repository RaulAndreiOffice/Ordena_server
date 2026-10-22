using Ordena.Backend.Data;
using Ordena.Backend.Infrastructure;

namespace Ordena.Backend.Services;

internal sealed class MaintenanceService(IAppPaths paths, DatabaseMigrator migrator) : IMaintenanceService
{
    // Step 5 adds a DB backup before migrating, journal recovery and cleaning the preview folder.
    public Task RunStartupAsync(CancellationToken ct) => Task.Run(async () =>
    {
        Directory.CreateDirectory(paths.Root);
        await migrator.MigrateAsync(ct).ConfigureAwait(false);
    }, ct);
}
