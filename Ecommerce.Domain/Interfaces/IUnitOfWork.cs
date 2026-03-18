namespace Ecommerce.Domain.Interfaces;

public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IUnitOfWorkTransaction> BeginTransactionAsync();
}

public interface IUnitOfWorkTransaction : IDisposable, IAsyncDisposable
{
    Task CommitAsync();
    Task RollbackAsync();
}
