using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferRacklessBalanceSplitSupportTests
{
    [Fact]
    public void AllocateNonSerial_splits_partial_available_into_located_and_shortage()
    {
        var chunks = ProductionTransferRacklessBalanceSplitSupport.AllocateNonSerial(3, 26, 2, 0);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(26, chunks[0].LocationId);
        Assert.Equal(2, chunks[0].Quantity);
        Assert.Null(chunks[1].LocationId);
        Assert.Equal(1, chunks[1].Quantity);
    }

    [Fact]
    public void AllocateNonSerial_covers_any_remaining_and_available_mix()
    {
        Assert.Single(ProductionTransferRacklessBalanceSplitSupport.AllocateNonSerial(8, 26, 8, 0));
        Assert.Equal(8, ProductionTransferRacklessBalanceSplitSupport.AllocateNonSerial(8, 26, 10, 0)[0].Quantity);

        var partial = ProductionTransferRacklessBalanceSplitSupport.AllocateNonSerial(10, 26, 1, 0);
        Assert.Equal(1, partial[0].Quantity);
        Assert.Equal(9, partial[1].Quantity);

        var none = ProductionTransferRacklessBalanceSplitSupport.AllocateNonSerial(3, 26, 0, 0);
        Assert.Single(none);
        Assert.Null(none[0].LocationId);
        Assert.Equal(3, none[0].Quantity);
    }

    [Fact]
    public void AllocateNonSerial_uses_line_reservation_when_available_pool_is_zero()
    {
        var chunks = ProductionTransferRacklessBalanceSplitSupport.AllocateNonSerial(3, 26, 0, 3);

        Assert.Single(chunks);
        Assert.Equal(26, chunks[0].LocationId);
        Assert.Equal(3, chunks[0].Quantity);
    }

    [Fact]
    public void ConsumePool_does_not_consume_reserved_quantity_from_shared_pool()
    {
        Assert.Equal(2, ProductionTransferRacklessBalanceSplitSupport.ConsumePool(2, 3, 3));
        Assert.Equal(0, ProductionTransferRacklessBalanceSplitSupport.ConsumePool(2, 2, 0));
        Assert.Equal(1, ProductionTransferRacklessBalanceSplitSupport.ConsumePool(5, 4, 0));
    }
}
