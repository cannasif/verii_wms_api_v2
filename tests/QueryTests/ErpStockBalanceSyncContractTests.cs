using System.Reflection;
using Hangfire;
using verii_wms_api_v2.Modules.ErpBalanceSync.Application;
using verii_wms_api_v2.Modules.ErpBalanceSync.Infrastructure;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ErpStockBalanceSyncContractTests
{
    [Fact]
    public void Full_sync_defaults_to_five_minutes_and_bounded_batches()
    {
        var options = new ErpStockBalanceSyncOptions();

        Assert.True(options.Enabled);
        Assert.Equal("*/5 * * * *", options.Cron);
        Assert.InRange(options.BatchSize, 50, 5000);
        Assert.Equal(0.50m, options.MinimumPreviousSourceRatio);
    }

    [Fact]
    public void Hangfire_job_has_no_automatic_retry_and_prevents_overlap()
    {
        var method = typeof(IErpStockBalanceSyncJobRunner).GetMethod(nameof(IErpStockBalanceSyncJobRunner.RunAsync));

        Assert.NotNull(method);
        var retry = method!.GetCustomAttribute<AutomaticRetryAttribute>();
        var concurrency = method.GetCustomAttribute<DisableConcurrentExecutionAttribute>();
        Assert.NotNull(retry);
        Assert.Equal(0, retry!.Attempts);
        Assert.NotNull(concurrency);
    }

    [Fact]
    public void Sql_pipeline_uses_temp_staging_change_only_batches_and_no_long_explicit_transaction()
    {
        var field = typeof(SqlServerErpStockBalanceSyncStore)
            .GetField("SyncSql", BindingFlags.Static | BindingFlags.NonPublic);
        var sql = Assert.IsType<string>(field?.GetRawConstantValue());

        Assert.Contains("CREATE TABLE #ERP_BALANCE_STAGE", sql, StringComparison.Ordinal);
        Assert.Contains("dbo.RII_FN_STOCK_BALANCE(NULL, NULL)", sql, StringComparison.Ordinal);
        Assert.Contains("OPENJSON(@TargetsJson)", sql, StringComparison.Ordinal);
        Assert.Contains("TOP (@BatchSize)", sql, StringComparison.Ordinal);
        Assert.Contains("WITH (ROWLOCK, UPDLOCK)", sql, StringComparison.Ordinal);
        Assert.Contains("MinimumPreviousSourceRatio", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Targeted_request_carries_multiple_warehouse_stock_dimensions_in_one_job()
    {
        var request = new ErpStockBalanceSyncJobRequest(
            "Targeted",
            "ErpPosting",
            [new(1, "STK-001"), new(2, "STK-002")],
            "WarehouseTransfer:42");

        Assert.Equal(2, request.Targets.Count);
        Assert.Equal("WarehouseTransfer:42", request.TriggerReference);
    }
}
