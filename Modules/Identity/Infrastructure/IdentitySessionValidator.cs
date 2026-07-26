using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using verii_wms_api_v2.Modules.Identity.Application;

namespace verii_wms_api_v2.Modules.Identity.Infrastructure;

public sealed class IdentitySessionValidator(
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    IConfiguration configuration) : IIdentitySessionValidator
{
    private static readonly Func<WmsDbContext, long, Task<SessionSnapshot?>> SessionQuery =
        EF.CompileAsyncQuery(
            (WmsDbContext context, long userId) =>
                context.Users
                    .AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => new SessionSnapshot(user.IsActive, user.TokenVersion))
                    .FirstOrDefault());

    private readonly ConcurrentDictionary<long, Lazy<Task<SessionSnapshot>>> _inflight = new();
    private readonly ConcurrentDictionary<long, long> _generations = new();
    private readonly TimeSpan _validCacheDuration = TimeSpan.FromSeconds(
        Math.Clamp(configuration.GetValue("Identity:TokenValidationCacheSeconds", 15), 1, 60));

    public async Task<bool> IsValidAsync(long userId, int tokenVersion)
    {
        var snapshot = await GetSnapshotAsync(userId);
        return snapshot.IsActive && snapshot.TokenVersion == tokenVersion;
    }

    public void Invalidate(long userId)
    {
        _generations.AddOrUpdate(userId, 1, static (_, generation) => generation + 1);
        cache.Remove(CacheKey(userId));
    }

    private async Task<SessionSnapshot> GetSnapshotAsync(long userId)
    {
        if (cache.TryGetValue<SessionSnapshot>(CacheKey(userId), out var cached) && cached is not null)
        {
            return cached;
        }

        var generation = _generations.GetOrAdd(userId, 0);
        var pending = _inflight.GetOrAdd(
            userId,
            _ => new Lazy<Task<SessionSnapshot>>(
                () => LoadSnapshotAsync(userId),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var snapshot = await pending.Value;
            if (_generations.GetOrAdd(userId, 0) == generation)
            {
                cache.Set(
                    CacheKey(userId),
                    snapshot,
                    snapshot.IsActive
                        ? _validCacheDuration
                        : TimeSpan.FromSeconds(Math.Min(5, _validCacheDuration.TotalSeconds)));
            }

            return snapshot;
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<long, Lazy<Task<SessionSnapshot>>>(userId, pending));
        }
    }

    private async Task<SessionSnapshot> LoadSnapshotAsync(long userId)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        return await SessionQuery(context, userId) ?? SessionSnapshot.Invalid;
    }

    private static string CacheKey(long userId) => $"identity:session-version:{userId}";

    private sealed record SessionSnapshot(bool IsActive, int TokenVersion)
    {
        public static SessionSnapshot Invalid { get; } = new(false, 0);
    }
}
