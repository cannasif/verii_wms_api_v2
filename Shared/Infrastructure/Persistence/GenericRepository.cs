using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Shared.Infrastructure.Persistence;

public sealed class GenericRepository<TEntity>(WmsDbContext context, IHttpContextAccessor httpContextAccessor) : IGenericRepository<TEntity>
    where TEntity : class
{
    private readonly DbSet<TEntity> _set = context.Set<TEntity>();

    public IQueryable<TEntity> Query(bool tracking = false, bool ignoreQueryFilters = false)
    {
        IQueryable<TEntity> query = _set;
        if (ignoreQueryFilters) query = query.IgnoreQueryFilters();
        return tracking ? query : query.AsNoTracking();
    }

    public async Task<TEntity?> FindByIdAsync(long id, bool tracking = false, CancellationToken cancellationToken = default)
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity)) ?? throw new InvalidOperationException($"{typeof(TEntity).Name} EF modelinde bulunamadı.");
        var key = entityType.FindPrimaryKey() ?? throw new InvalidOperationException($"{typeof(TEntity).Name} primary key içermiyor.");
        if (key.Properties.Count != 1) throw new NotSupportedException("Composite key kullanan entity için özel repository kullanılmalıdır.");
        var property = key.Properties[0];
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var member = Expression.Property(parameter, property.Name);
        var value = Expression.Convert(Expression.Constant(id), member.Type);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(member, value), parameter);
        return await (tracking ? _set.AsQueryable() : _set.AsNoTracking()).FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, bool tracking = false, CancellationToken cancellationToken = default) =>
        (tracking ? _set.AsQueryable() : _set.AsNoTracking()).FirstOrDefaultAsync(predicate, cancellationToken);

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => _set.AnyAsync(predicate, cancellationToken);

    public Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        predicate is null ? _set.CountAsync(cancellationToken) : _set.CountAsync(predicate, cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        StampCreated(entity);
        await _set.AddAsync(entity, cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var items = entities.ToList();
        foreach (var entity in items) StampCreated(entity);
        await _set.AddRangeAsync(items, cancellationToken);
    }

    public void Update(TEntity entity) { StampUpdated(entity); _set.Update(entity); }
    public void Remove(TEntity entity) => _set.Remove(entity);
    public void RemoveRange(IEnumerable<TEntity> entities) => _set.RemoveRange(entities);

    public async Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await FindByIdAsync(id, true, cancellationToken);
        if (entity is not BaseEntity baseEntity) return false;
        baseEntity.IsDeleted = true;
        baseEntity.DeletedDate = DateTime.UtcNow;
        baseEntity.DeletedBy = CurrentUserId();
        return true;
    }

    private void StampCreated(TEntity entity)
    {
        if (entity is not BaseEntity baseEntity) return;
        baseEntity.CreatedDate ??= DateTime.UtcNow;
        baseEntity.CreatedBy ??= CurrentUserId();
        baseEntity.IsDeleted = false;
    }

    private void StampUpdated(TEntity entity)
    {
        if (entity is not BaseEntity baseEntity) return;
        baseEntity.UpdatedDate = DateTime.UtcNow;
        baseEntity.UpdatedBy = CurrentUserId();
    }

    private long? CurrentUserId() => long.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
