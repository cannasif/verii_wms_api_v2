using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseOutboundOperationGuardTests
{
    [Fact]
    public void Serial_tracked_line_requires_a_serial_number()
    {
        var line = Line(StockTrackingType.Serial);
        var exception = Assert.Throws<AppException>(() =>
            Validate(line, Request(1), WarehouseOutboundOperationPhase.Pick));
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void Serial_tracked_line_requires_unit_quantity()
    {
        var line = Line(StockTrackingType.Serial);
        var exception = Assert.Throws<AppException>(() =>
            Validate(line, Request(2, serial: "SER-001"), WarehouseOutboundOperationPhase.Pick));
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void Planned_tracking_must_match_during_pick()
    {
        var line = Line(StockTrackingType.LotAndSerial);
        line.Trackings.Add(Tracking("LOT-A", "SER-001", 1));
        var exception = Assert.Throws<AppException>(() =>
            Validate(line, Request(1, "LOT-B", "SER-001"), WarehouseOutboundOperationPhase.Pick));
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public void Pack_cannot_use_a_serial_that_was_not_picked()
    {
        var line = Line(StockTrackingType.Serial);
        line.Trackings.Add(Tracking(null, "SER-001", 1, picked: 1));
        var exception = Assert.Throws<AppException>(() =>
            Validate(line, Request(1, serial: "SER-002"), WarehouseOutboundOperationPhase.Pack));
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public void Pack_cannot_exceed_picked_tracking_quantity()
    {
        var line = Line(StockTrackingType.Lot);
        line.Trackings.Add(Tracking("LOT-A", null, 10, picked: 4, packed: 3));
        var exception = Assert.Throws<AppException>(() =>
            Validate(line, Request(2, lot: "LOT-A"), WarehouseOutboundOperationPhase.Pack));
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public void Matching_tracking_with_available_quantity_is_accepted()
    {
        var line = Line(StockTrackingType.LotAndSerial);
        line.Trackings.Add(Tracking("LOT-A", "SER-001", 1, picked: 1));
        Validate(line, Request(1, "LOT-A", "SER-001"), WarehouseOutboundOperationPhase.Pack);
    }

    [Fact]
    public void Pick_source_location_must_match_planned_tracking_location()
    {
        var line = Line(StockTrackingType.Serial);
        var tracking = Tracking(null, "SER-001", 1);
        tracking.SourceLocationId = 10;
        line.Trackings.Add(tracking);
        var exception = Assert.Throws<AppException>(() =>
            Validate(line, Request(1, serial: "SER-001", sourceLocationId: 11), WarehouseOutboundOperationPhase.Pick));
        Assert.Equal(409, exception.StatusCode);
    }

    private static void Validate(
        WarehouseOutboundLine line,
        WarehouseOutboundOperationLineRequest request,
        WarehouseOutboundOperationPhase phase) =>
        WarehouseOutboundOperationGuard.ValidateTrackingDimension(
            new WarehouseOutboundHeader
            {
                PackingPolicy = WarehouseOutboundPackingPolicy.Required,
                RequireLoadingConfirmation = true
            },
            line,
            request,
            phase);

    private static WarehouseOutboundLine Line(StockTrackingType type) => new()
    {
        Id = 1,
        LineNo = 1,
        TrackingType = type,
        RequestedQuantity = 10,
        UnitCode = "ADET"
    };

    private static WarehouseOutboundTracking Tracking(
        string? lot,
        string? serial,
        decimal planned,
        decimal picked = 0,
        decimal packed = 0) => new()
    {
        LotNo = lot,
        SerialNo = serial,
        PlannedQuantity = planned,
        PickedQuantity = picked,
        PackedQuantity = packed
    };

    private static WarehouseOutboundOperationLineRequest Request(
        decimal quantity,
        string? lot = null,
        string? serial = null,
        long? sourceLocationId = 10) =>
        new(1, quantity, sourceLocationId, 20, lot, serial, null);
}
