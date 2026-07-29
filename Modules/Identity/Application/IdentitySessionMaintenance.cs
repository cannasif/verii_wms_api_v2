using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

namespace verii_wms_api_v2.Modules.Identity.Application;

public interface IIdentitySessionMaintenance
{
    Task<int> DeleteObsoleteSessionsAsync(CancellationToken cancellationToken = default);
}

public sealed class IdentitySessionMaintenance(
    WmsDbContext dbContext,
    IConfiguration configuration,
    ILogger<IdentitySessionMaintenance> logger) : IIdentitySessionMaintenance
{
    public async Task<int> DeleteObsoleteSessionsAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = Math.Clamp(
            configuration.GetValue("Identity:RefreshTokenRetentionDays", 7),
            1,
            90);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = await dbContext.RefreshTokenSessions
            .IgnoreQueryFilters()
            .Where(session =>
                session.ExpiresAt <= cutoff
                || session.RevokedAt.HasValue && session.RevokedAt.Value <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation(
            "Obsolete identity sessions deleted. Count={DeletedCount} RetentionDays={RetentionDays}",
            deleted,
            retentionDays);
        return deleted;
    }
}
