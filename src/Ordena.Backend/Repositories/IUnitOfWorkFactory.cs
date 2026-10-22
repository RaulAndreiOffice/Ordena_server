namespace Ordena.Backend.Repositories;

internal interface IUnitOfWorkFactory
{
    IUnitOfWork Create();
}
