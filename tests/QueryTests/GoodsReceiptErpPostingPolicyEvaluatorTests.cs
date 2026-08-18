using System.Text.Json;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptErpPostingPolicyEvaluatorTests
{
    [Fact]
    public void Goods_receipt_item_slip_is_open_by_default()
    {
        var options = new NetsisRestOptions();

        var invoiceType = ErpPostingService.ResolveGoodsReceiptInvoiceType(options);

        Assert.Equal(NetsisItemSlipInvoiceType.DomesticOpen, invoiceType);
        Assert.Equal(2, (int)invoiceType);
    }

    [Fact]
    public void Foreign_goods_receipt_serializes_import_file_metadata_for_netsis()
    {
        var source = new GoodsReceiptHeader
        {
            TradeType = GoodsReceiptTradeType.Foreign,
            ImportFileNumber = "ITH-2026-001"
        };
        var target = new NetsisItemSlipHeader
        {
            Tipi = NetsisItemSlipInvoiceType.DomesticOpen
        };

        ErpPostingService.ApplyGoodsReceiptTradeClassification(source, target);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(target));
        Assert.Equal(8, json.RootElement.GetProperty("TIPI").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("EXPORTTYPE").GetInt32());
        Assert.Equal("ITH-2026-001", json.RootElement.GetProperty("EXPORTREFNO").GetString());
    }

    [Fact]
    public void Domestic_goods_receipt_omits_foreign_trade_metadata()
    {
        var source = new GoodsReceiptHeader
        {
            TradeType = GoodsReceiptTradeType.Domestic
        };
        var target = new NetsisItemSlipHeader
        {
            Tipi = NetsisItemSlipInvoiceType.DomesticOpen,
            ExportType = 1,
            ExportReferenceNumber = "STALE"
        };

        ErpPostingService.ApplyGoodsReceiptTradeClassification(source, target);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(target));
        Assert.Equal(2, json.RootElement.GetProperty("TIPI").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("EXPORTTYPE", out _));
        Assert.False(json.RootElement.TryGetProperty("EXPORTREFNO", out _));
    }

    [Fact]
    public void Invalid_goods_receipt_invoice_type_is_rejected_before_erp_posting()
    {
        var options = new NetsisRestOptions
        {
            GoodsReceiptInvoiceType = (NetsisItemSlipInvoiceType)99
        };

        Assert.Throws<InvalidOperationException>(
            () => ErpPostingService.ResolveGoodsReceiptInvoiceType(options));
    }

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
    public void Rejected_quality_is_a_completed_decision_for_purchase_receipt_posting(
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
    public void Any_quality_plan_releases_erp_after_a_failed_terminal_decision()
    {
        var eligible = GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.NotRequired,
            OperationQualityStatus.Failed,
            GoodsReceiptErpPostingPolicy.AfterReceipt,
            GoodsReceiptErpQualityGatePolicy.AnyQualityPlan,
            hasRuleBasedQualityPlan: false,
            hasManualQualityPlan: true);

        Assert.True(eligible);
    }

    [Fact]
    public void Any_quality_plan_blocks_manual_quality_even_when_posting_policy_is_after_receipt()
    {
        var eligible = GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.NotRequired,
            OperationQualityStatus.Pending,
            GoodsReceiptErpPostingPolicy.AfterReceipt,
            GoodsReceiptErpQualityGatePolicy.AnyQualityPlan,
            hasRuleBasedQualityPlan: false,
            hasManualQualityPlan: true);

        Assert.False(eligible);
    }

    [Fact]
    public void Rule_based_gate_does_not_block_a_manual_plan_by_itself()
    {
        var eligible = GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.NotRequired,
            OperationQualityStatus.Pending,
            GoodsReceiptErpPostingPolicy.AfterReceipt,
            GoodsReceiptErpQualityGatePolicy.RuleBasedOnly,
            hasRuleBasedQualityPlan: false,
            hasManualQualityPlan: true);

        Assert.True(eligible);
    }

    [Fact]
    public void Rule_based_gate_blocks_stock_quality_until_it_passes()
    {
        Assert.False(GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.NotRequired,
            OperationQualityStatus.InProgress,
            GoodsReceiptErpPostingPolicy.AfterReceipt,
            GoodsReceiptErpQualityGatePolicy.RuleBasedOnly,
            hasRuleBasedQualityPlan: true,
            hasManualQualityPlan: false));

        Assert.True(GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
            WarehouseOperationStatus.Processed,
            OperationApprovalStatus.NotRequired,
            OperationQualityStatus.Passed,
            GoodsReceiptErpPostingPolicy.AfterReceipt,
            GoodsReceiptErpQualityGatePolicy.RuleBasedOnly,
            hasRuleBasedQualityPlan: true,
            hasManualQualityPlan: false));
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
