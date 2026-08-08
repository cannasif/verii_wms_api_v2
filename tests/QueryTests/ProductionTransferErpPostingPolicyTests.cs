using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProductionTransferErpPostingPolicyTests
{
    [Theory]
    [InlineData(ProductionTransferWorkflowStatus.Completed, WarehouseTransferStatus.Completed)]
    [InlineData(ProductionTransferWorkflowStatus.CompletedWithShortage, WarehouseTransferStatus.CompletedWithShortage)]
    public void After_handover_posts_completed_transfer_to_erp(
        ProductionTransferWorkflowStatus workflowStatus,
        WarehouseTransferStatus transferStatus)
    {
        Assert.True(ProductionTransferErpPostingPolicyEvaluator.IsEligible(
            ProductionTransferErpPostingPolicy.AfterHandover,
            workflowStatus,
            transferStatus,
            ErpIntegrationStatus.Pending));
        Assert.True(ErpPostingService.IsWarehouseTransferReadyForErp(transferStatus));
    }

    [Theory]
    [InlineData(ProductionTransferErpPostingPolicy.Disabled)]
    [InlineData(ProductionTransferErpPostingPolicy.Manual)]
    public void Non_automatic_policy_does_not_post_during_handover(
        ProductionTransferErpPostingPolicy policy)
    {
        Assert.False(ProductionTransferErpPostingPolicyEvaluator.IsEligible(
            policy,
            ProductionTransferWorkflowStatus.Completed,
            WarehouseTransferStatus.Completed,
            ErpIntegrationStatus.Pending));
    }

    [Theory]
    [InlineData(ErpIntegrationStatus.Processing)]
    [InlineData(ErpIntegrationStatus.Succeeded)]
    [InlineData(ErpIntegrationStatus.CommitUncertain)]
    [InlineData(ErpIntegrationStatus.Cancelled)]
    public void Automatic_post_does_not_duplicate_terminal_or_in_progress_erp_state(
        ErpIntegrationStatus erpStatus)
    {
        Assert.False(ProductionTransferErpPostingPolicyEvaluator.IsEligible(
            ProductionTransferErpPostingPolicy.AfterHandover,
            ProductionTransferWorkflowStatus.Completed,
            WarehouseTransferStatus.Completed,
            erpStatus));
    }
}
