using verii_wms_api_v2.Modules.ErpMirror.Application;
using verii_wms_api_v2.Modules.SystemManagement.Application;

namespace verii_wms_api_v2.Modules.StockBalance.Application;

public sealed class StockBalanceJobRunner(IStockBalanceService balances, IHangfireExecutionLogService logs, ILogger<StockBalanceJobRunner> logger) : IStockBalanceJobRunner
{
    public async Task ReconcileAndRepairAsync(CancellationToken cancellationToken = default)
    {
        var logId = await logs.StartAsync("stock-balance-reconciliation", "Hangfire", CancellationToken.None);
        try
        {
            var summary = await balances.GetReconciliationSummaryAsync(cancellationToken);
            var repaired = summary.MismatchCount > 0 ? await balances.RebuildAsync(cancellationToken) : null;
            await logs.CompleteAsync(logId, new MirrorSyncResult("StockBalanceProjection", summary.LedgerGroupCount,
                repaired?.LocationRows ?? 0, repaired?.WarehouseRows ?? 0, summary.MismatchCount), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Stock balance reconciliation failed.");
            await logs.FailAsync(logId, exception, CancellationToken.None);
            throw;
        }
    }
}
