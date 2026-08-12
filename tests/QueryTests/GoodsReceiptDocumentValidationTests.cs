using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Application.Validation;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptDocumentValidationTests
{
    [Theory]
    [InlineData(true, GoodsReceiptLocationSelectionPolicy.ReceivingOrStagingOnly)]
    [InlineData(false, GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation)]
    public void Putaway_block_is_the_single_source_for_location_selection(
        bool blockPutawayUntilQualityDecision,
        GoodsReceiptLocationSelectionPolicy expected)
    {
        Assert.Equal(
            expected,
            GoodsReceiptLocationPolicy.ResolveSelectionPolicy(
                blockPutawayUntilQualityDecision));
    }

    [Theory]
    [InlineData(LocationTypes.Receiving, true)]
    [InlineData(LocationTypes.Staging, true)]
    [InlineData(LocationTypes.Rack, false)]
    [InlineData(LocationTypes.Shelf, false)]
    public void Strict_location_policy_only_allows_receiving_and_staging(
        string locationType,
        bool expected)
    {
        var location = new WarehouseLocation
        {
            WarehouseId = 10,
            LocationType = locationType,
            IsActive = true
        };

        Assert.Equal(
            expected,
            GoodsReceiptLocationPolicy.IsAllowed(
                GoodsReceiptLocationSelectionPolicy.ReceivingOrStagingOnly,
                location,
                warehouseId: 10));
    }

    [Theory]
    [InlineData(LocationTypes.Receiving)]
    [InlineData(LocationTypes.Staging)]
    [InlineData(LocationTypes.Rack)]
    [InlineData(LocationTypes.Shelf)]
    [InlineData(LocationTypes.Cell)]
    public void Any_active_location_policy_allows_every_active_location_in_selected_warehouse(
        string locationType)
    {
        var location = new WarehouseLocation
        {
            WarehouseId = 10,
            LocationType = locationType,
            IsActive = true
        };

        Assert.True(GoodsReceiptLocationPolicy.IsAllowed(
            GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation,
            location,
            warehouseId: 10));
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 11)]
    public void Any_active_location_policy_rejects_inactive_or_other_warehouse_locations(
        bool isActive,
        long warehouseId)
    {
        var location = new WarehouseLocation
        {
            WarehouseId = warehouseId,
            LocationType = LocationTypes.Rack,
            IsActive = isActive
        };

        Assert.False(GoodsReceiptLocationPolicy.IsAllowed(
            GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation,
            location,
            warehouseId: 10));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Non_quality_or_non_blocking_receipt_line_can_use_any_active_warehouse_location(
        bool requiresQuality,
        bool blockPutawayUntilQualityDecision)
    {
        var rack = new WarehouseLocation
        {
            WarehouseId = 10,
            LocationType = LocationTypes.Rack,
            IsActive = true
        };

        Assert.True(GoodsReceiptLocationPolicy.IsAllowedForReceiptLine(
            GoodsReceiptLocationSelectionPolicy.ReceivingOrStagingOnly,
            rack,
            warehouseId: 10,
            requiresQuality,
            blockPutawayUntilQualityDecision));
    }

    [Fact]
    public void Quality_gated_receipt_line_still_obeys_strict_receiving_policy()
    {
        var rack = new WarehouseLocation
        {
            WarehouseId = 10,
            LocationType = LocationTypes.Rack,
            IsActive = true
        };

        Assert.False(GoodsReceiptLocationPolicy.IsAllowedForReceiptLine(
            GoodsReceiptLocationSelectionPolicy.ReceivingOrStagingOnly,
            rack,
            warehouseId: 10,
            requiresQuality: true,
            blockPutawayUntilQualityDecision: true));
    }

    [Fact]
    public void Order_based_receipt_requires_exactly_one_waybill_type()
    {
        var missing = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(null, null, new DateOnly(2026, 7, 28)));
        var both = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(
                "000000000000001", "GIB202600000001", new DateOnly(2026, 7, 28)));

        Assert.Contains("yalnızca biri", missing.Message);
        Assert.Contains("yalnızca biri", both.Message);
    }

    [Theory]
    [InlineData("irs202600000001", null)]
    [InlineData(null, "gib2026ab000001")]
    [InlineData("AB2", null)]
    [InlineData(null, "ERS202600029")]
    [InlineData(null, "GIB*2026/AB0001")]
    [InlineData("AB-2", null)]
    public void Order_based_receipt_accepts_and_normalizes_valid_waybill(
        string? waybillNo,
        string? electronicWaybillNo)
    {
        var result = GoodsReceiptService.NormalizeDocumentReference(
            waybillNo, electronicWaybillNo, new DateOnly(2026, 7, 28));

        Assert.Equal(PurchaseWaybillNumberPolicy.Normalize(waybillNo), result.WaybillNo);
        Assert.Equal(PurchaseWaybillNumberPolicy.Normalize(electronicWaybillNo), result.ElectronicWaybillNo);
    }

    [Fact]
    public void Order_based_receipt_rejects_invalid_number_or_missing_date()
    {
        var invalid = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(
                "ABC", null, new DateOnly(2026, 7, 28)));
        var missingDate = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(
                "000000000000001", null, null));

        Assert.Contains("15 karakter", invalid.Message);
        Assert.Contains("tarihi zorunludur", missingDate.Message);
    }

    [Fact]
    public void Electronic_waybill_requires_exactly_fifteen_printable_characters()
    {
        var tooLong = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(
                null, "GIB2026AB0000001", new DateOnly(2026, 7, 28)));
        var invalidCharacter = Assert.Throws<AppException>(() =>
            GoodsReceiptService.NormalizeDocumentReference(
                null, "GIB2026AB000\t00", new DateOnly(2026, 7, 28)));

        Assert.Contains("15 karakter", tooLong.Message);
        Assert.Contains("15 karakter", invalidCharacter.Message);
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
    public void Quality_gated_receipt_cannot_be_received_directly_into_normal_putaway_location()
    {
        var receiving = new WarehouseLocation
        {
            LocationType = LocationTypes.Receiving,
            IsPutaway = true
        };
        var rack = new WarehouseLocation
        {
            LocationType = LocationTypes.Rack,
            IsPutaway = true
        };

        var exception = Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateQualityReceivingLocations(
                requiresQuality: true,
                blockPutawayUntilQualityDecision: true,
                selectedLocations: [receiving, rack]));

        Assert.Contains("kabul veya staging", exception.Message);
    }

    [Theory]
    [InlineData(LocationTypes.Receiving)]
    [InlineData(LocationTypes.Staging)]
    public void Quality_gated_receipt_accepts_receiving_areas_even_when_putaway_capability_is_enabled(
        string locationType)
    {
        GoodsReceiptOperationsService.ValidateQualityReceivingLocations(
            requiresQuality: true,
            blockPutawayUntilQualityDecision: true,
            selectedLocations:
            [
                new WarehouseLocation
                {
                    LocationType = locationType,
                    IsPutaway = true
                }
            ]);
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
            selectedLocations:
            [
                new WarehouseLocation
                {
                    LocationType = LocationTypes.Rack,
                    IsPutaway = true
                }
            ]);
    }

    [Fact]
    public void Already_inspected_steel_does_not_enter_a_second_quality_queue()
    {
        Assert.False(GoodsReceiptOperationsService.RequiresQuality(
            qualityAlreadyApproved: true,
            anyStockPolicyRequiresQuality: true));
    }

    [Fact]
    public void Uninspected_receipt_requires_an_applicable_stock_or_group_rule()
    {
        Assert.True(GoodsReceiptOperationsService.RequiresQuality(
            qualityAlreadyApproved: false,
            anyStockPolicyRequiresQuality: true));
        Assert.False(GoodsReceiptOperationsService.RequiresQuality(
            qualityAlreadyApproved: false,
            anyStockPolicyRequiresQuality: false));
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
