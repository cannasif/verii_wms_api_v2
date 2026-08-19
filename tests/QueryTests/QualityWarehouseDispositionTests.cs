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
    [InlineData(33L, 22L, 44L, 33L)]
    [InlineData(null, 22L, 44L, 22L)]
    [InlineData(null, null, 44L, 44L)]
    [InlineData(null, null, null, null)]
    public void Accepted_target_uses_goods_receipt_selected_location(
        long? receivingLocationId,
        long? putawayLocationId,
        long? headerReceivingLocationId,
        long? expected)
    {
        Assert.Equal(expected, QualityService.ResolveAcceptedLocationId(
            receivingLocationId,
            putawayLocationId,
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
    public void Inspection_accepted_decision_uses_goods_receipt_location()
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

        Assert.Equal([43], required);
        Assert.DoesNotContain(95, required);
    }

    [Fact]
    public void Inspection_accepted_decision_uses_warehouse_matrix_accepted_location()
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
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes = new Dictionary<long, QualityWarehouseRoute>
        {
            [1] = new() { SourceWarehouseId = 1, AcceptedLocationId = 20 },
            [2] = new() { SourceWarehouseId = 2, AcceptedLocationId = 99 }
        };

        var required = QualityService.ResolveRequiredDecisionTargetLocationIds(
            [new QualityService.QualityDecisionPart(
                inspectionLine,
                QualityDecision.Accepted,
                1)],
            new Dictionary<long, GoodsReceiptLine> { [receiptLine.Id] = receiptLine },
            new QualityParameter { DefaultAcceptedLocationId = 95 },
            routes,
            headerReceivingLocationId: 43,
            []);

        Assert.Equal([20], required);
        Assert.DoesNotContain(43, required);
        Assert.DoesNotContain(95, required);
        Assert.DoesNotContain(99, required);
    }

    [Fact]
    public void Inspection_accepted_destination_falls_back_to_the_receipt_when_the_warehouse_route_is_empty()
    {
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes = new Dictionary<long, QualityWarehouseRoute>
        {
            [1] = new() { SourceWarehouseId = 1 }
        };

        var resolved = QualityService.ResolveInspectionAcceptedLocationId(
            routes,
            sourceWarehouseId: 1,
            defaultReceivingLocationId: 43,
            defaultPutawayLocationId: 22,
            headerReceivingLocationId: 44);

        Assert.Equal(43, resolved);
    }

    [Fact]
    public void Inspection_accepted_destination_uses_only_the_matching_warehouse_route()
    {
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes = new Dictionary<long, QualityWarehouseRoute>
        {
            [1] = new() { SourceWarehouseId = 1, AcceptedLocationId = 20 },
            [2] = new() { SourceWarehouseId = 2, AcceptedLocationId = 99 }
        };

        var resolved = QualityService.ResolveInspectionAcceptedLocationId(
            routes,
            sourceWarehouseId: 1,
            defaultReceivingLocationId: 43,
            defaultPutawayLocationId: 22,
            headerReceivingLocationId: 44);

        Assert.Equal(20, resolved);
    }

    [Fact]
    public void Inspection_accepted_decision_uses_putaway_then_header_when_receiving_is_missing()
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
            DefaultPutawayLocationId = 22
        };

        var required = QualityService.ResolveRequiredDecisionTargetLocationIds(
            [new QualityService.QualityDecisionPart(
                inspectionLine,
                QualityDecision.Accepted,
                1)],
            new Dictionary<long, GoodsReceiptLine> { [receiptLine.Id] = receiptLine },
            new QualityParameter { DefaultAcceptedLocationId = 95 },
            new Dictionary<long, QualityWarehouseRoute>(),
            headerReceivingLocationId: 44,
            []);

        Assert.Equal([22], required);
        Assert.DoesNotContain(44, required);
        Assert.DoesNotContain(95, required);
    }

    [Fact]
    public void Inspection_route_does_not_inherit_branch_defaults()
    {
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes = new Dictionary<long, QualityWarehouseRoute>
        {
            [100] = new()
            {
                SourceWarehouseId = 100,
                AcceptedLocationId = 20
            }
        };

        var resolved = QualityService.ResolveInspectionWarehouseRoute(routes, 100);

        Assert.Null(resolved.QualityLocationId);
        Assert.Equal(20, resolved.AcceptedLocationId);
        Assert.Null(resolved.QuarantineLocationId);
        Assert.Null(resolved.RejectLocationId);
        Assert.Null(QualityService.ResolveInspectionWarehouseRoute(routes, 399).AcceptedLocationId);
    }

    [Fact]
    public void Inspection_quarantine_prefers_warehouse_matrix_then_section_list()
    {
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes = new Dictionary<long, QualityWarehouseRoute>
        {
            [1] = new() { SourceWarehouseId = 1, QuarantineLocationId = 30 },
            [2] = new() { SourceWarehouseId = 2, QuarantineLocationId = 99 }
        };
        var section = new[]
        {
            new QualityQuarantineDestinationDto(1, 10, 8, 800, "Diger", "K-8", "Karantina 8", 1, true, true)
        };

        Assert.Equal(30, QualityService.ResolveInspectionQuarantineLocationId(routes, section, 1));
        Assert.Equal(10, QualityService.ResolveInspectionQuarantineLocationId(
            new Dictionary<long, QualityWarehouseRoute>(),
            section,
            1));
        Assert.Null(QualityService.ResolveInspectionQuarantineLocationId(
            new Dictionary<long, QualityWarehouseRoute>(),
            [],
            1));
    }
}
