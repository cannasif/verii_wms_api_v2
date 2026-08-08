using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdRequestStateMachineTests
{
    [Fact]
    public void Unresolved_group_request_waits_for_stock_selection()
    {
        var request = Request(Line(requested: 1));

        KkdRequestStateMachine.Refresh(request, DateTimeOffset.UtcNow);

        Assert.Equal(KkdRequestStatus.AwaitingStockSelection, request.Status);
        Assert.Equal(KkdRequestLineStatus.AwaitingStockSelection, request.Lines.Single().Status);
    }

    [Fact]
    public void Resolved_line_is_ready_to_prepare()
    {
        var request = Request(Line(requested: 2, stockId: 41));

        KkdRequestStateMachine.Refresh(request, DateTimeOffset.UtcNow);

        Assert.Equal(KkdRequestStatus.ReadyToPrepare, request.Status);
        Assert.NotNull(request.ReadyAtUtc);
    }

    [Fact]
    public void Allocated_quantity_marks_request_in_preparation()
    {
        var request = Request(Line(requested: 3, stockId: 41, allocated: 2));

        KkdRequestStateMachine.Refresh(request, DateTimeOffset.UtcNow);

        Assert.Equal(KkdRequestStatus.InPreparation, request.Status);
        Assert.Equal(KkdRequestLineStatus.InPreparation, request.Lines.Single().Status);
    }

    [Theory]
    [InlineData(1, "PartiallyDelivered")]
    [InlineData(3, "Completed")]
    public void Delivered_quantity_drives_partial_and_completed_states(decimal delivered, string expected)
    {
        var request = Request(Line(requested: 3, stockId: 41, delivered: delivered));

        KkdRequestStateMachine.Refresh(request, DateTimeOffset.UtcNow);

        Assert.Equal(expected, request.Status.ToString());
        Assert.Equal(expected, request.Lines.Single().Status.ToString());
    }

    private static KkdRequest Request(KkdRequestLine line) => new() { Lines = [line] };

    private static KkdRequestLine Line(decimal requested, long? stockId = null, decimal allocated = 0, decimal delivered = 0) => new()
    {
        GroupCode = "AYAKKABI",
        StockId = stockId,
        RequestedQuantity = requested,
        AllocatedQuantity = allocated,
        DeliveredQuantity = delivered
    };
}
