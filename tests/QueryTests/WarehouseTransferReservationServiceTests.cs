using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class WarehouseTransferReservationServiceTests
{
    [Fact]
    public void UsesTransferReservations_is_true_for_production_transfer_with_none_policy()
    {
        var header = new WarehouseTransferHeader
        {
            ReservationPolicy = WarehouseTransferReservationPolicy.None,
            BusinessContext = WarehouseTransferBusinessContext.ProductionMaterialSupply,
        };

        Assert.True(WarehouseTransferReservationService.UsesTransferReservations(header));
    }

    [Fact]
    public void UsesTransferReservations_is_false_for_standard_transfer_with_none_policy()
    {
        var header = new WarehouseTransferHeader
        {
            ReservationPolicy = WarehouseTransferReservationPolicy.None,
            BusinessContext = WarehouseTransferBusinessContext.InterWarehouse,
        };

        Assert.False(WarehouseTransferReservationService.UsesTransferReservations(header));
    }
}
