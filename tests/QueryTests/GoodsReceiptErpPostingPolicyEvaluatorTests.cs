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
    [InlineData(GoodsReceiptErpPostingPolicy.AfterQualityApproval)]
    [InlineData(GoodsReceiptErpPostingPolicy.AfterAllApprovals)]
    public void Rejected_quality_is_a_completed_decision_for_erp_posting(
        GoodsReceiptErpPostingPolicy postingPolicy)
    {
        var eligible = GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.Approved,
            OperationQualityStatus.Failed,
            postingPolicy);

        Assert.True(eligible);
    }

    [Fact]
    public void Rejected_quantity_remains_part_of_the_physical_goods_receipt()
    {
        var line = new GoodsReceiptLine
        {
            ReceivedQuantity = 10,
            AcceptedQuantity = 6,
            RejectedQuantity = 4
        };

        Assert.Equal(10, ErpPostingService.GoodsReceiptQuantityForErp(line));
    }

    [Fact]
    public void Normal_waybill_number_is_used_as_the_netsis_document_number()
    {
        var header = new GoodsReceiptHeader
        {
            DocumentNo = "GR1202600000044",
            WaybillNo = "IRS20260000001"
        };

        Assert.Equal("IRS20260000001", ErpPostingService.ResolveGoodsReceiptErpDocumentNo(header));
    }

    [Fact]
    public void Electronic_waybill_number_has_priority_for_the_netsis_document_number()
    {
        var header = new GoodsReceiptHeader
        {
            DocumentNo = "GR1202600000044",
            WaybillNo = "IRS20260000001",
            ElectronicWaybillNo = "GIB2026AB000001"
        };

        Assert.Equal("GIB2026AB000001", ErpPostingService.ResolveGoodsReceiptErpDocumentNo(header));
    }

    [Fact]
    public void Internal_wms_document_number_is_never_a_goods_receipt_erp_fallback()
    {
        var header = new GoodsReceiptHeader { DocumentNo = "GR1202600000044" };

        var exception = Assert.Throws<verii_wms_api_v2.Shared.Application.Exceptions.AppException>(
            () => ErpPostingService.ResolveGoodsReceiptErpDocumentNo(header));

        Assert.Contains("irsaliye", exception.Message, StringComparison.OrdinalIgnoreCase);
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
