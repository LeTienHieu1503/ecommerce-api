using Ecommerce.Domain.Interfaces;
using Ecommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecommerce.Infrastructure.Data;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private bool _disposed;

    public EfUnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync()
    {
        var efTransaction = await _context.Database.BeginTransactionAsync();
        return new EfUnitOfWorkTransaction(efTransaction);
    }

    public void Dispose()
    {
        // Do NOT dispose _context here.
        // ApplicationDbContext is registered as Scoped and its lifetime is managed by DI.
        // Disposing it here causes a double-dispose on subsequent accesses.
        _disposed = true;
    }
}

public class EfUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    private readonly IDbContextTransaction _transaction;

    public EfUnitOfWorkTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public async Task CommitAsync() => await _transaction.CommitAsync();
    public async Task RollbackAsync() => await _transaction.RollbackAsync();
    public void Dispose() => _transaction.Dispose();
    public async ValueTask DisposeAsync() => await _transaction.DisposeAsync();
}
