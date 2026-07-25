using System.Linq.Expressions;

namespace verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

public interface IGenericRepository<TEntity> where TEntity : class
{
    IQueryable<TEntity> Query(bool tracking = false, bool ignoreQueryFilters = false);
    Task<TEntity?> FindByIdAsync(long id, bool tracking = false, CancellationToken cancellationToken = default);
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, bool tracking = false, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    void RemoveRange(IEnumerable<TEntity> entities);
    Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);
}
