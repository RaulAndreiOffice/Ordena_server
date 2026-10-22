namespace Ordena.Backend.Services;

public interface IMaintenanceService
{
    // Called once at app startup, before anything else touches the database:
    // creates the app data folder and the database, or upgrades it to the latest migration.
    Task RunStartupAsync(CancellationToken ct);
}
