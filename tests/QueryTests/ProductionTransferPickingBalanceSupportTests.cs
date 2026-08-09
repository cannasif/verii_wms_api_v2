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
}
