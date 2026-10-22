using Microsoft.EntityFrameworkCore;
using Ordena.Backend.Data;

namespace Ordena.Backend.Repositories;

internal sealed class UnitOfWorkFactory(IDbContextFactory<AppDbContext> contextFactory) : IUnitOfWorkFactory
{
    public IUnitOfWork Create() => new UnitOfWork(contextFactory.CreateDbContext());
}
