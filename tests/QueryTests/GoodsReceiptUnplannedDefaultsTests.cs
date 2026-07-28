using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptUnplannedDefaultsTests
{
    [Fact]
    public void Orderless_and_direct_receipts_always_use_lowest_priority()
    {
        var request = new CreateManualGoodsReceiptRequest(
            Guid.NewGuid(), "0", 1, 1, 1, 1, new DateOnly(2026, 7, 28),
            "000000000000001", new DateOnly(2026, 7, 28), null,
            null, null, null, null, null, null, null,
            null, null, GoodsReceiptLabelStrategy.None, GoodsReceiptExecutionMode.Manual,
            5, null, null, null, []);

        var normalized = GoodsReceiptOperationsService.ApplyUnplannedDefaults(request);

        Assert.Equal((byte)1, normalized.Priority);
        Assert.Equal((byte)5, request.Priority);
    }
}
