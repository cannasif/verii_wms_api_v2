using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Shared.Infrastructure.Persistence;

public sealed class UnitOfWork(WmsDbContext context, IHttpContextAccessor httpContextAccessor) : IUnitOfWork
{
    private readonly ConcurrentDictionary<Type, object> _repositories = new();
    private IDbContextTransaction? _transaction;

    public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class =>
        (IGenericRepository<TEntity>)_repositories.GetOrAdd(typeof(TEntity), _ => new GenericRepository<TEntity>(context, httpContextAccessor));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        if (_transaction is not null) throw new InvalidOperationException("Aktif bir transaction zaten bulunuyor.");
        _transaction = await context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) throw new InvalidOperationException("Aktif transaction bulunamadı.");
        try { await _transaction.CommitAsync(cancellationToken); }
        catch { await RollbackTransactionAsync(cancellationToken); throw; }
        finally { if (_transaction is not null) { await _transaction.DisposeAsync(); _transaction = null; } }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;
        try { await _transaction.RollbackAsync(cancellationToken); }
        finally { await _transaction.DisposeAsync(); _transaction = null; }
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        // Participating services share the scoped DbContext/UoW. The outer orchestrator owns commit/rollback;
        // nested services only execute inside that ambient transaction.
        if (_transaction is not null) return await operation(cancellationToken);
        await BeginTransactionAsync(isolationLevel, cancellationToken);
        try { var result = await operation(cancellationToken); await CommitTransactionAsync(cancellationToken); return result; }
        catch { await RollbackTransactionAsync(cancellationToken); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await _transaction.DisposeAsync();
        _transaction = null;
    }
}
