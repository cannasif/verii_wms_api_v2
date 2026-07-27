using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptErpPostingPolicyEvaluatorTests
{
    [Fact]
    public void After_receipt_is_eligible_without_approval_or_quality_decisions()
    {
        var eligible = GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.Pending,
            OperationQualityStatus.Pending,
            GoodsReceiptErpPostingPolicy.AfterReceipt);

        Assert.True(eligible);
    }

    [Theory]
    [InlineData(GoodsReceiptErpPostingPolicy.AfterReceiptApproval)]
    [InlineData(GoodsReceiptErpPostingPolicy.AfterQualityApproval)]
    [InlineData(GoodsReceiptErpPostingPolicy.AfterAllApprovals)]
    public void Non_required_gates_are_treated_as_completed(
        GoodsReceiptErpPostingPolicy postingPolicy)
    {
        var eligible = GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.NotRequired,
            OperationQualityStatus.NotRequired,
            postingPolicy);

        Assert.True(eligible);
    }

    [Fact]
    public void After_all_approvals_waits_for_every_required_gate()
    {
        Assert.False(GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.Approved,
            OperationQualityStatus.InProgress,
            GoodsReceiptErpPostingPolicy.AfterAllApprovals));

        Assert.True(GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.Approved,
            OperationQualityStatus.Passed,
            GoodsReceiptErpPostingPolicy.AfterAllApprovals));
    }

    [Theory]
    [InlineData(WarehouseOperationStatus.Draft)]
    [InlineData(WarehouseOperationStatus.InProgress)]
    [InlineData(WarehouseOperationStatus.Cancelled)]
    public void Physical_receipt_must_be_completed_first(WarehouseOperationStatus status)
    {
        var eligible = GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            status,
            OperationApprovalStatus.NotRequired,
            OperationQualityStatus.NotRequired,
            GoodsReceiptErpPostingPolicy.AfterReceipt);

        Assert.False(eligible);
    }
}
