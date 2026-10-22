using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ordena.Backend.Data;

// Used only by `dotnet ef`: the Backend is a class library with no host to build the context.
// `migrations add` never opens this database; the real path comes from AppPaths at runtime.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=ordena.db")
            .Options;

        return new AppDbContext(options);
    }
}
