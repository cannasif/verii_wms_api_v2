using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityWarehouseDispositionTests
{
    [Fact]
    public void Same_warehouse_disposition_remains_an_internal_location_movement()
    {
        Assert.False(QualityService.RequiresDat(10, 10));
    }

    [Fact]
    public void Different_warehouse_disposition_requires_a_DAT()
    {
        Assert.True(QualityService.RequiresDat(10, 20));
    }

    [Theory]
    [InlineData(WarehouseOperationStatus.Processed, true)]
    [InlineData(WarehouseOperationStatus.Completed, true)]
    [InlineData(WarehouseOperationStatus.Draft, false)]
    [InlineData(WarehouseOperationStatus.Released, false)]
    [InlineData(WarehouseOperationStatus.InProgress, false)]
    [InlineData(WarehouseOperationStatus.PartiallyProcessed, false)]
    [InlineData(WarehouseOperationStatus.Cancelled, false)]
    public void Quality_inventory_disposition_requires_a_physically_completed_receipt(
        WarehouseOperationStatus status,
        bool expected)
    {
        Assert.Equal(expected, QualityService.IsReceiptReadyForQualityDisposition(status));
    }

    [Theory]
    [InlineData(11L, 22L, 33L, 44L, 11L)]
    [InlineData(null, 22L, 33L, 44L, 22L)]
    [InlineData(null, null, 33L, 44L, 33L)]
    [InlineData(null, null, null, 44L, 44L)]
    [InlineData(null, null, null, null, null)]
    public void Accepted_target_prefers_warehouse_route_then_goods_receipt_defaults(
        long? routeLocationId,
        long? putawayLocationId,
        long? receivingLocationId,
        long? headerReceivingLocationId,
        long? expected)
    {
        Assert.Equal(expected, QualityService.ResolveAcceptedLocationId(
            routeLocationId,
            putawayLocationId,
            receivingLocationId,
            headerReceivingLocationId));
    }

    [Fact]
    public void Warehouse_route_overrides_only_configured_targets_and_inherits_the_rest()
    {
        var parameter = new QualityParameter
        {
            DefaultQualityLocationId = 1,
            DefaultAcceptedLocationId = 2,
            DefaultQuarantineLocationId = 3,
            DefaultRejectLocationId = 4
        };
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes = new Dictionary<long, QualityWarehouseRoute>
        {
            [100] = new()
            {
                SourceWarehouseId = 100,
                AcceptedLocationId = 20,
                QuarantineLocationId = 30
            }
        };

        var resolved = QualityService.ResolveWarehouseRouteDefaults(parameter, routes, 100);

        Assert.Equal(1, resolved.QualityLocationId);
        Assert.Equal(20, resolved.AcceptedLocationId);
        Assert.Equal(30, resolved.QuarantineLocationId);
        Assert.Equal(4, resolved.RejectLocationId);
    }

    [Fact]
    public void Unmatched_warehouse_uses_branch_level_fallbacks()
    {
        var parameter = new QualityParameter
        {
            DefaultQualityLocationId = 1,
            DefaultAcceptedLocationId = 2,
            DefaultQuarantineLocationId = 3,
            DefaultRejectLocationId = 4
        };

        var resolved = QualityService.ResolveWarehouseRouteDefaults(
            parameter,
            new Dictionary<long, QualityWarehouseRoute>(),
            399);

        Assert.Equal(1, resolved.QualityLocationId);
        Assert.Equal(2, resolved.AcceptedLocationId);
        Assert.Equal(3, resolved.QuarantineLocationId);
        Assert.Equal(4, resolved.RejectLocationId);
    }

    [Fact]
    public void Quality_hold_receipt_uses_the_warehouse_route_waiting_location()
    {
        var header = new GoodsReceiptHeader { HoldInventoryUntilQualityDecision = true, QualityLocationId = 1001 };
        var line = new GoodsReceiptLine
        {
            RequireQualityControl = true,
            DefaultReceivingLocationId = 1002
        };

        var locationId = GoodsReceiptOperationsService.ResolveQualityInventoryLocationId(
            line, header, line.DefaultReceivingLocationId.Value);

        Assert.Equal(1001, locationId);
    }

    [Fact]
    public void Receipt_without_quality_hold_keeps_the_requested_location()
    {
        var header = new GoodsReceiptHeader { HoldInventoryUntilQualityDecision = false, QualityLocationId = 1001 };
        var line = new GoodsReceiptLine
        {
            RequireQualityControl = false,
            DefaultReceivingLocationId = 1002
        };

        var locationId = GoodsReceiptOperationsService.ResolveQualityInventoryLocationId(
            line, header, line.DefaultReceivingLocationId.Value);

        Assert.Equal(1002, locationId);
    }

    [Fact]
    public void Explicit_quality_target_does_not_require_stale_receipt_fallback_location()
    {
        var inspectionLine = new QualityInspectionLine
        {
            Id = 1,
            GoodsReceiptLineId = 10,
            StockId = 100,
            StockCodeSnapshot = "STK-1",
            Quantity = 1
        };
        var receiptLine = new GoodsReceiptLine
        {
            Id = 10,
            TargetWarehouseId = 1,
            DefaultReceivingLocationId = 43
        };

        var required = QualityService.ResolveRequiredDecisionTargetLocationIds(
            [new QualityService.QualityDecisionPart(
                inspectionLine,
                QualityDecision.Accepted,
                1,
                TargetLocationId: 95)],
            new Dictionary<long, GoodsReceiptLine> { [receiptLine.Id] = receiptLine },
            new QualityParameter(),
            new Dictionary<long, QualityWarehouseRoute>(),
            headerReceivingLocationId: 43,
            []);

        Assert.Equal([95], required);
        Assert.DoesNotContain(43, required);
    }

    [Fact]
    public void Accepted_quality_decision_without_explicit_target_uses_configured_fallback()
    {
        var inspectionLine = new QualityInspectionLine
        {
            Id = 1,
            GoodsReceiptLineId = 10,
            StockId = 100,
            StockCodeSnapshot = "STK-1",
            Quantity = 1
        };
        var receiptLine = new GoodsReceiptLine
        {
            Id = 10,
            TargetWarehouseId = 1,
            DefaultReceivingLocationId = 43
        };
        var parameter = new QualityParameter { DefaultAcceptedLocationId = 95 };

        var required = QualityService.ResolveRequiredDecisionTargetLocationIds(
            [new QualityService.QualityDecisionPart(
                inspectionLine,
                QualityDecision.Accepted,
                1)],
            new Dictionary<long, GoodsReceiptLine> { [receiptLine.Id] = receiptLine },
            parameter,
            new Dictionary<long, QualityWarehouseRoute>(),
            headerReceivingLocationId: 43,
            []);

        Assert.Equal([95], required);
    }
}
