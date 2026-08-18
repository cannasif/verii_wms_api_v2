using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferPickingBalanceSupportTests
{
    [Fact]
    public void ResolvePickableQuantity_uses_reserved_quantity_when_available_is_zero()
    {
        var line = new WarehouseTransferLine
        {
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = 5,
            ReservedQuantity = 4,
        };

        var balance = new LocationStockBalance
        {
            LocationId = 5,
            StockId = 13,
            UnitCode = "ADET",
            Quantity = 4,
            ReservedQuantity = 4,
            AvailableQuantity = 0,
            StockStatus = "Available",
        };

        var pickable = ProductionTransferPickingBalanceSupport.ResolvePickableQuantity(line, 5, balance);

        Assert.Equal(4, pickable);
    }

    [Fact]
    public void ResolvePickableQuantity_prefers_line_reservation_over_available_quantity()
    {
        var line = new WarehouseTransferLine
        {
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = 5,
            ReservedQuantity = 15,
        };

        var balance = new LocationStockBalance
        {
            LocationId = 5,
            StockId = 13,
            UnitCode = "ADET",
            Quantity = 20,
            ReservedQuantity = 15,
            AvailableQuantity = 5,
            StockStatus = "Available",
        };

        var pickable = ProductionTransferPickingBalanceSupport.ResolvePickableQuantity(line, 5, balance);

        Assert.Equal(15, pickable);
    }

    [Fact]
    public void ResolvePickableQuantity_falls_back_to_available_when_line_is_not_reserved()
    {
        var line = new WarehouseTransferLine
        {
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = 5,
            ReservedQuantity = 0,
        };

        var balance = new LocationStockBalance
        {
            LocationId = 5,
            StockId = 13,
            UnitCode = "ADET",
            Quantity = 8,
            ReservedQuantity = 0,
            AvailableQuantity = 5,
            StockStatus = "Available",
        };

        var pickable = ProductionTransferPickingBalanceSupport.ResolvePickableQuantity(line, 5, balance);

        Assert.Equal(5, pickable);
    }

    [Fact]
    public void ResolvePickableQuantity_uses_tracking_reservation_for_serial_line()
    {
        var line = new WarehouseTransferLine
        {
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = 5,
            Trackings =
            [
                new WarehouseTransferTracking
                {
                    SerialNo = "UTG-9",
                    SourceLocationId = 5,
                    ReservedQuantity = 1,
                    PlannedQuantity = 1,
                },
            ],
        };

        var balance = new LocationStockBalance
        {
            LocationId = 5,
            StockId = 13,
            UnitCode = "ADET",
            SerialNo = "UTG-9",
            Quantity = 1,
            ReservedQuantity = 1,
            AvailableQuantity = 0,
            StockStatus = "Available",
        };

        var pickable = ProductionTransferPickingBalanceSupport.ResolvePickableQuantity(line, 5, balance);

        Assert.Equal(1, pickable);
    }

    [Fact]
    public void ApplyRacklessCanPick_clears_can_pick_when_location_has_no_available_or_reserved()
    {
        var line = new WarehouseTransferLine
        {
            Id = 20,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = 5,
            ReservedQuantity = 0,
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var rows = new[]
        {
            new ProductionTransferPickingRowDto(
                400, 20, 2, 5, "01/026", 13, "01/013", "Test", null, 3, 3, 0, true),
        };
        var balances = new[]
        {
            new LocationStockBalance
            {
                LocationId = 5,
                StockId = 13,
                UnitCode = "ADET",
                Quantity = 3,
                ReservedQuantity = 3,
                AvailableQuantity = 0,
                StockStatus = "Available",
            },
        };

        var updated = ProductionTransferPickingBalanceSupport.ApplyRacklessCanPick(header, rows, balances);

        Assert.False(Assert.Single(updated).CanPick);
    }

    [Fact]
    public void ApplyRacklessCanPick_keeps_can_pick_when_line_is_reserved_at_location()
    {
        var line = new WarehouseTransferLine
        {
            Id = 20,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = 5,
            ReservedQuantity = 3,
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var rows = new[]
        {
            new ProductionTransferPickingRowDto(
                400, 20, 2, 5, "01/026", 13, "01/013", "Test", null, 3, 3, 0, true),
        };
        var balances = new[]
        {
            new LocationStockBalance
            {
                LocationId = 5,
                StockId = 13,
                UnitCode = "ADET",
                Quantity = 3,
                ReservedQuantity = 3,
                AvailableQuantity = 0,
                StockStatus = "Available",
            },
        };

        var updated = ProductionTransferPickingBalanceSupport.ApplyRacklessCanPick(header, rows, balances);

        Assert.True(Assert.Single(updated).CanPick);
    }

    [Fact]
    public void ApplyRacklessCanPick_keeps_can_pick_when_available_quantity_exists()
    {
        var line = new WarehouseTransferLine
        {
            Id = 20,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = 5,
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var rows = new[]
        {
            new ProductionTransferPickingRowDto(
                400, 20, 2, 5, "01/026", 13, "01/013", "Test", null, 3, 3, 0, true),
        };
        var balances = new[]
        {
            new LocationStockBalance
            {
                LocationId = 5,
                StockId = 13,
                UnitCode = "ADET",
                Quantity = 8,
                ReservedQuantity = 0,
                AvailableQuantity = 8,
                StockStatus = "Available",
            },
        };

        var updated = ProductionTransferPickingBalanceSupport.ApplyRacklessCanPick(header, rows, balances);

        Assert.True(Assert.Single(updated).CanPick);
    }

    [Fact]
    public void ApplyRacklessCanPick_leaves_historical_and_completed_rows_unchanged()
    {
        var line = new WarehouseTransferLine
        {
            Id = 20,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = 5,
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var historical = new ProductionTransferPickingRowDto(
            400, 20, 2, 5, "01/026", 13, "01/013", "Test", null, 2, 0, 2, false, true);
        var completed = historical with { IsHistorical = false };

        var updated = ProductionTransferPickingBalanceSupport.ApplyRacklessCanPick(
            header, [historical, completed], []);

        Assert.Equal(historical, updated[0]);
        Assert.Equal(completed, updated[1]);
    }
}
