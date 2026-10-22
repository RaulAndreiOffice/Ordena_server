using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Models;

namespace Ordena.Backend.Data;

internal sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<FileOperation> FileOperations => Set<FileOperation>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                property.SetValueConverter(UtcDateTimeConverter.Instance);
        }
    }
}
