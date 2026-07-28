using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptDocumentValidationTests
{
    [Fact]
    public void Order_based_receipt_requires_exactly_one_waybill_type()
    {
        var missing = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(null, null, new DateOnly(2026, 7, 28)));
        var both = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(
                "000000000000001", "GIB2026000000001", new DateOnly(2026, 7, 28)));

        Assert.Contains("yalnızca biri", missing.Message);
        Assert.Contains("yalnızca biri", both.Message);
    }

    [Theory]
    [InlineData("000000000000001", null)]
    [InlineData(null, "gib2026000000001")]
    public void Order_based_receipt_accepts_and_normalizes_valid_waybill(
        string? waybillNo,
        string? electronicWaybillNo)
    {
        var result = GoodsReceiptService.NormalizeDocumentReference(
            waybillNo, electronicWaybillNo, new DateOnly(2026, 7, 28));

        Assert.Equal(waybillNo, result.WaybillNo);
        Assert.Equal(electronicWaybillNo?.ToUpperInvariant(), result.ElectronicWaybillNo);
    }

    [Fact]
    public void Order_based_receipt_rejects_invalid_number_or_missing_date()
    {
        var invalid = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(
                "123", null, new DateOnly(2026, 7, 28)));
        var missingDate = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(
                "000000000000001", null, null));

        Assert.Contains("15 rakam", invalid.Message);
        Assert.Contains("tarihi zorunludur", missingDate.Message);
    }

    [Fact]
    public void Direct_receipt_rejects_internal_pre_generated_label_mode()
    {
        Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateDirectLabelMode(
                GoodsReceiptLabelStrategy.PreGenerate,
                GoodsReceiptExecutionMode.PreGeneratedLabel));
    }

    [Fact]
    public void Direct_supplier_label_requires_matching_execution_mode()
    {
        Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateDirectLabelMode(
                GoodsReceiptLabelStrategy.SupplierLabel,
                GoodsReceiptExecutionMode.Manual));

        GoodsReceiptOperationsService.ValidateDirectLabelMode(
            GoodsReceiptLabelStrategy.SupplierLabel,
            GoodsReceiptExecutionMode.SupplierLabel);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Only_orderless_direct_receipt_requires_unplanned_permission(
        bool direct,
        bool hasOrderSources,
        bool expected)
    {
        Assert.Equal(
            expected,
            GoodsReceiptOperationsService.RequiresUnplannedReceiptPermission(direct, hasOrderSources));
    }

    [Fact]
    public void Quality_gated_receipt_cannot_be_received_directly_into_putaway_location()
    {
        var exception = Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateQualityReceivingLocations(
                requiresQuality: true,
                blockPutawayUntilQualityDecision: true,
                selectedLocationsArePutaway: [false, true]));

        Assert.Contains("kabul veya staging", exception.Message);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Putaway_location_is_allowed_when_quality_gate_does_not_block_it(
        bool requiresQuality,
        bool blockPutawayUntilQualityDecision)
    {
        GoodsReceiptOperationsService.ValidateQualityReceivingLocations(
            requiresQuality,
            blockPutawayUntilQualityDecision,
            selectedLocationsArePutaway: [true]);
    }

    [Fact]
    public void Already_inspected_steel_does_not_enter_a_second_quality_queue()
    {
        Assert.False(GoodsReceiptOperationsService.RequiresQuality(
            qualityAlreadyApproved: true,
            receiptPolicyRequiresQuality: true,
            anyStockPolicyRequiresQuality: true));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Uninspected_receipt_respects_quality_policies(
        bool receiptPolicyRequiresQuality,
        bool anyStockPolicyRequiresQuality)
    {
        Assert.True(GoodsReceiptOperationsService.RequiresQuality(
            qualityAlreadyApproved: false,
            receiptPolicyRequiresQuality,
            anyStockPolicyRequiresQuality));
    }

    [Fact]
    public void Import_flow_accepts_missing_waybill_reference()
    {
        GoodsReceiptOperationsService.ValidateDocumentReference(
            null,
            null,
            null,
            GoodsReceiptExecutionMode.Import);
    }

    [Fact]
    public void Manual_flow_requires_one_waybill_reference()
    {
        var exception = Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateDocumentReference(
                null,
                null,
                null,
                GoodsReceiptExecutionMode.Manual));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("zorunludur", exception.Message);
    }

    [Fact]
    public void Two_waybill_reference_types_are_rejected()
    {
        var exception = Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateDocumentReference(
                "123456789012345",
                "ABC2026000000001",
                new DateOnly(2026, 7, 27),
                GoodsReceiptExecutionMode.Import));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("birlikte girilemez", exception.Message);
    }

    [Fact]
    public void Supplied_waybill_reference_requires_date()
    {
        var exception = Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateDocumentReference(
                "123456789012345",
                null,
                null,
                GoodsReceiptExecutionMode.Import));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("tarihi zorunludur", exception.Message);
    }
}
