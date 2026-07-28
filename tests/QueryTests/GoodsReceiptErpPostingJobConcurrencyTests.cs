using Hangfire;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptErpPostingJobConcurrencyTests
{
    [Fact]
    public void Posting_job_contract_serializes_netsis_item_slip_calls()
    {
        var method = typeof(IGoodsReceiptErpPostingJob)
            .GetMethod(nameof(IGoodsReceiptErpPostingJob.PostIfEligibleAsync));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttributes(
            typeof(DisableConcurrentExecutionAttribute),
            inherit: true).SingleOrDefault());
    }
}
