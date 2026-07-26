using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

namespace verii_wms_api_v2.Shared.Host.Startup;

public sealed class StartupReadinessState
{
    private volatile bool _isReady;

    public bool IsReady => _isReady;
    public void MarkReady() => _isReady = true;
}

public sealed class StartupReadinessHealthCheck(StartupReadinessState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            state.IsReady
                ? HealthCheckResult.Healthy("Application startup warmup completed.")
                : HealthCheckResult.Unhealthy("Application startup warmup is still running."));
}

public sealed class StartupWarmupHostedService(
    IServiceScopeFactory scopeFactory,
    StartupReadinessState readiness,
    IConfiguration configuration,
    ILogger<StartupWarmupHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Performance:WarmDatabaseOnStartup", true))
        {
            readiness.MarkReady();
            return;
        }

        var timer = Stopwatch.StartNew();
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        // Pay the EF model, first query and SQL connection-pool costs before
        // the load balancer is allowed to send warehouse operators here.
        _ = context.Model;
        await context.Users
            .AsNoTracking()
            .OrderBy(user => user.Id)
            .Select(user => new { user.Id, user.IsActive, user.TokenVersion })
            .Take(1)
            .ToListAsync(cancellationToken);

        readiness.MarkReady();
        logger.LogInformation(
            "Application startup warmup completed in {DurationMs} ms.",
            timer.ElapsedMilliseconds);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
