using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Shared.Infrastructure.Persistence;

public sealed class GenericRepository<TEntity>(WmsDbContext context, IHttpContextAccessor httpContextAccessor) : IGenericRepository<TEntity>
    where TEntity : class
{
    private readonly DbSet<TEntity> _set = context.Set<TEntity>();
    private static readonly bool IsBranchScopedEntity =
        typeof(BaseEntity).IsAssignableFrom(typeof(TEntity))
        && !IsGlobalEntity(typeof(TEntity));

    public IQueryable<TEntity> Query(bool tracking = false, bool ignoreQueryFilters = false)
    {
        IQueryable<TEntity> query = _set;
        if (ignoreQueryFilters) query = query.IgnoreQueryFilters();
        query = ApplyAuthenticatedBranchScope(query);
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
        var query = ApplyAuthenticatedBranchScope(_set);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, bool tracking = false, CancellationToken cancellationToken = default) =>
        (tracking ? ApplyAuthenticatedBranchScope(_set) : ApplyAuthenticatedBranchScope(_set).AsNoTracking())
        .FirstOrDefaultAsync(predicate, cancellationToken);

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        ApplyAuthenticatedBranchScope(_set).AnyAsync(predicate, cancellationToken);

    public Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        predicate is null
            ? ApplyAuthenticatedBranchScope(_set).CountAsync(cancellationToken)
            : ApplyAuthenticatedBranchScope(_set).CountAsync(predicate, cancellationToken);

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

    public void Update(TEntity entity)
    {
        EnsureExistingEntityBelongsToAuthenticatedBranch(entity);
        StampUpdated(entity);
        _set.Update(entity);
    }

    public void Remove(TEntity entity)
    {
        EnsureExistingEntityBelongsToAuthenticatedBranch(entity);
        _set.Remove(entity);
    }

    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        var items = entities.ToList();
        foreach (var entity in items) EnsureExistingEntityBelongsToAuthenticatedBranch(entity);
        _set.RemoveRange(items);
    }

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
        var branchCode = CurrentBranchCode();
        if (IsBranchScopedEntity && branchCode is not null)
            baseEntity.BranchCode = branchCode;
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

    private string? CurrentBranchCode()
    {
        if (!IsBranchScopedEntity)
            return null;

        var branchCode = httpContextAccessor.HttpContext?.User
            .FindFirstValue(JwtTokenIssuer.BranchCodeClaim)
            ?.Trim();
        return string.IsNullOrWhiteSpace(branchCode) ? null : branchCode;
    }

    private IQueryable<TEntity> ApplyAuthenticatedBranchScope(IQueryable<TEntity> query)
    {
        var branchCode = CurrentBranchCode();
        if (branchCode is null)
            return query;

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var branchProperty = Expression.Property(parameter, nameof(BaseEntity.BranchCode));
        var predicate = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(branchProperty, Expression.Constant(branchCode)),
            parameter);
        return query.Where(predicate);
    }

    private void EnsureExistingEntityBelongsToAuthenticatedBranch(TEntity entity)
    {
        var branchCode = CurrentBranchCode();
        if (branchCode is null || entity is not BaseEntity baseEntity)
            return;
        if (!string.Equals(baseEntity.BranchCode, branchCode, StringComparison.OrdinalIgnoreCase))
            throw AppException.Forbidden("Bu kayıt giriş yapılan şubeye ait değil.");
    }

    private static bool IsGlobalEntity(Type entityType)
    {
        var entityNamespace = entityType.Namespace ?? string.Empty;
        return entityNamespace.Contains(".Modules.Identity.", StringComparison.Ordinal)
            || entityNamespace.Contains(".Modules.AccessControl.", StringComparison.Ordinal)
            || entityNamespace.Contains(".Modules.Audit.", StringComparison.Ordinal)
            || entityNamespace.Contains(".Modules.SystemManagement.", StringComparison.Ordinal);
    }
}
