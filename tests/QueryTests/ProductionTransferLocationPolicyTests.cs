using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferLocationPolicyTests
{
    [Fact]
    public void ResolveHandoverTargetLocationId_UsesTargetPutaway_WhenWarehousesDiffer()
    {
        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = 1,
            TargetWarehouseId = 3,
            TargetPutawayLocationId = 300,
        };
        var line = new WarehouseTransferLine
        {
            LineNo = 1,
            DefaultTargetLocationId = 100,
        };

        var targetLocationId = ProductionTransferLocationPolicy.ResolveHandoverTargetLocationId(header, line);

        Assert.Equal(300, targetLocationId);
    }

    [Fact]
    public void ResolveHandoverTargetLocationId_UsesLineDefault_WhenSameWarehouse()
    {
        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = 1,
            TargetWarehouseId = 1,
            TargetPutawayLocationId = 300,
        };
        var line = new WarehouseTransferLine
        {
            LineNo = 1,
            DefaultTargetLocationId = 100,
        };

        var targetLocationId = ProductionTransferLocationPolicy.ResolveHandoverTargetLocationId(header, line);

        Assert.Equal(100, targetLocationId);
    }

    [Fact]
    public void ResolveHandoverTargetLocationId_Throws_WhenCrossWarehousePutawayMissing()
    {
        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = 1,
            TargetWarehouseId = 3,
            TargetPutawayLocationId = null,
        };
        var line = new WarehouseTransferLine { LineNo = 2, DefaultTargetLocationId = 100 };

        var error = Assert.Throws<AppException>(() =>
            ProductionTransferLocationPolicy.ResolveHandoverTargetLocationId(header, line));

        Assert.Equal(409, error.StatusCode);
    }
}
