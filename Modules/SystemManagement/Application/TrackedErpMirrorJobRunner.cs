using verii_wms_api_v2.Modules.ErpMirror.Application;

namespace verii_wms_api_v2.Modules.SystemManagement.Application;

public interface ITrackedErpMirrorJobRunner
{
    Task RunWarehousesAsync(CancellationToken cancellationToken = default);
    Task RunStocksAsync(CancellationToken cancellationToken = default);
    Task RunCustomersAsync(CancellationToken cancellationToken = default);
    Task RunConfigurationCodesAsync(CancellationToken cancellationToken = default);
}

public sealed class TrackedErpMirrorJobRunner(
    IErpMirrorService mirrorService,
    IHangfireExecutionLogService logService,
    ILogger<TrackedErpMirrorJobRunner> logger) : ITrackedErpMirrorJobRunner
{
    public Task RunWarehousesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("erp-warehouse-mirror-sync", () => mirrorService.SyncWarehousesAsync(cancellationToken), cancellationToken);

    public Task RunStocksAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("erp-stock-mirror-sync", () => mirrorService.SyncStocksAsync(cancellationToken), cancellationToken);

    public Task RunCustomersAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("erp-customer-mirror-sync", () => mirrorService.SyncCustomersAsync(cancellationToken), cancellationToken);

    public Task RunConfigurationCodesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("erp-configuration-code-mirror-sync", () => mirrorService.SyncConfigurationCodesAsync(cancellationToken), cancellationToken);

    private async Task ExecuteAsync(string jobKey, Func<Task<MirrorSyncResult>> action, CancellationToken cancellationToken)
    {
        var logId = await logService.StartAsync(jobKey, "Hangfire", CancellationToken.None);
        try
        {
            var result = await action();
            await logService.CompleteAsync(logId, result, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Tracked Hangfire job failed: {JobKey}", jobKey);
            await logService.FailAsync(logId, exception, CancellationToken.None);
            throw;
        }
    }
}
