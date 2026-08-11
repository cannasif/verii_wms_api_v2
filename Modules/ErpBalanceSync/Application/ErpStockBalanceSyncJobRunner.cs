using verii_wms_api_v2.Modules.ErpMirror.Application;
using verii_wms_api_v2.Modules.SystemManagement.Application;

namespace verii_wms_api_v2.Modules.ErpBalanceSync.Application;

public sealed class ErpStockBalanceSyncJobRunner(
    IErpStockBalanceSyncStore store,
    IHangfireExecutionLogService executionLogs,
    ILogger<ErpStockBalanceSyncJobRunner> logger) : IErpStockBalanceSyncJobRunner
{
    public async Task RunAsync(ErpStockBalanceSyncJobRequest request, CancellationToken cancellationToken = default)
    {
        var logId = await executionLogs.StartAsync("erp-stock-balance-sync", request.TriggerSource, CancellationToken.None);
        long runId = 0;
        try
        {
            runId = await store.StartRunAsync(request, cancellationToken);
            var result = await store.SynchronizeAsync(runId, request, cancellationToken);
            await store.CompleteRunAsync(result, CancellationToken.None);
            await executionLogs.CompleteAsync(logId,
                new MirrorSyncResult("ErpStockBalance", result.SourceCount, result.InsertedCount,
                    result.UpdatedCount, result.MissingCount), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "ERP stock balance synchronization failed. RunId={RunId} Mode={Mode} Trigger={TriggerSource}",
                runId, request.Mode, request.TriggerSource);
            if (runId > 0)
                await store.FailRunAsync(runId, exception, CancellationToken.None);
            await executionLogs.FailAsync(logId, exception, CancellationToken.None);
            throw;
        }
    }
}
