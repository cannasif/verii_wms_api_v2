using Hangfire;
using verii_wms_api_v2.Modules.ErpMirror.Application;
using verii_wms_api_v2.Modules.Packing.Application;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.SystemManagement.Application;

namespace verii_wms_api_v2.Shared.Host.BackgroundJobs;

public sealed class RecurringJobRegistrationHostedService(
    IRecurringJobManager recurringJobs,
    IConfiguration configuration,
    ILogger<RecurringJobRegistrationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Hangfire:RegisterRecurringJobs", true))
        {
            return;
        }

        var initialDelay = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue("Hangfire:RecurringJobRegistrationDelaySeconds", 2), 0, 30));
        if (initialDelay > TimeSpan.Zero)
        {
            await Task.Delay(initialDelay, stoppingToken);
        }

        var retryDelay = TimeSpan.FromSeconds(15);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RegisterJobs();
                logger.LogInformation("Hangfire recurring jobs registered.");
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Hangfire recurring job registration failed. Retrying in {RetrySeconds} seconds.",
                    retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, stoppingToken);
            }
        }
    }

    private void RegisterJobs()
    {
        recurringJobs.AddOrUpdate<ITrackedErpMirrorJobRunner>(
            "erp-warehouse-mirror-sync",
            service => service.RunWarehousesAsync(CancellationToken.None),
            Cron.Hourly);
        recurringJobs.AddOrUpdate<ITrackedErpMirrorJobRunner>(
            "erp-stock-mirror-sync",
            service => service.RunStocksAsync(CancellationToken.None),
            Cron.Hourly);
        recurringJobs.AddOrUpdate<ITrackedErpMirrorJobRunner>(
            "erp-customer-mirror-sync",
            service => service.RunCustomersAsync(CancellationToken.None),
            Cron.Hourly);
        recurringJobs.RemoveIfExists("erp-yap-code-mirror-sync");
        recurringJobs.AddOrUpdate<ITrackedErpMirrorJobRunner>(
            "erp-configuration-code-mirror-sync",
            service => service.RunConfigurationCodesAsync(CancellationToken.None),
            Cron.Hourly);
        recurringJobs.AddOrUpdate<IStockBalanceJobRunner>(
            "stock-balance-reconciliation",
            service => service.ReconcileAndRepairAsync(CancellationToken.None),
            Cron.Daily(2, 30));
        recurringJobs.AddOrUpdate<IPackingPrintQueueJobRunner>(
            "packing-print-queue",
            service => service.DispatchPendingAsync(CancellationToken.None),
            Cron.Minutely);
    }
}
