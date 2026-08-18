using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferCancellationReturnRemainderSupportTests
{
    [Fact]
    public void IsProductionCancellationReturnTask_distinguishes_iptaliade_from_warehouse_cancel_return()
    {
        var iptaliade = new WarehouseTransferTask
        {
            TaskType = WarehouseTransferTaskType.CancellationReturn,
            TaskNo = "MK202600000113-IPTALIADE1",
        };
        var warehouseReturn = new WarehouseTransferTask
        {
            TaskType = WarehouseTransferTaskType.CancellationReturn,
            TaskNo = "MK202600000113-C01",
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsProductionCancellationReturnTask(iptaliade));
        Assert.False(ProductionWorkOrderTransferGrouping.IsProductionCancellationReturnTask(warehouseReturn));
    }

    [Fact]
    public void MatchesTab_after_iptaliade_keeps_unassigned_remainder_out_of_picking_and_cancelled()
    {
        var iptaliade = new WarehouseTransferTask
        {
            Id = 50,
            TaskNo = "TR-100-IPTALIADE1",
            TaskType = WarehouseTransferTaskType.CancellationReturn,
            Status = WarehouseTransferTaskStatus.Completed,
        };
        var kalan = new WarehouseTransferTask
        {
            Id = 60,
            TaskNo = "TR-100-2",
            TaskType = WarehouseTransferTaskType.Pick,
            Status = WarehouseTransferTaskStatus.Open,
            PreviousTaskId = 50,
            Assignments = [],
        };
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Draft,
            Tasks = [iptaliade, kalan],
        };
        var link = new ProductionTransferHeaderLink
        {
            WorkflowStatus = ProductionTransferWorkflowStatus.Planned,
            WarehouseTransferHeader = header,
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsCancellationReturnEligibleForRemainderFlow(iptaliade));
        Assert.True(ProductionWorkOrderTransferGrouping.IsPostCancellationReturnUnassignedPickTask(kalan, [iptaliade, kalan]));
        Assert.False(ProductionWorkOrderTransferGrouping.IsUnassignedCreatedPickTask(kalan, link, [iptaliade, kalan]));
        Assert.True(ProductionWorkOrderTransferGrouping.HasOnlyPostCancellationReturnUnassignedRemainder(header, link));
        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking, header, link));
        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Cancelled, header, link));
    }

    [Fact]
    public void Reactivate_after_iptaliade_clears_awaiting_handover_without_cancelling_header()
    {
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.AwaitingHandover,
            CancelledAtUtc = DateTimeOffset.UtcNow,
            CancelledBy = 9,
            Lines =
            [
                new WarehouseTransferLine
                {
                    RequestedQuantity = 10,
                    PickedQuantity = 0,
                    Status = WarehouseTransferLineStatus.Picked,
                },
            ],
        };
        var link = new ProductionTransferHeaderLink
        {
            ProductionOrderNo = "WO-113",
            WorkflowStatus = ProductionTransferWorkflowStatus.AwaitingHandover,
            WarehouseTransferHeader = header,
        };

        ProductionTransferCancellationReturnRemainderSupport.ReactivateUnlinkedTransferAfterCancellationReturn(
            header,
            link,
            actor: 1,
            utcNow: DateTime.UtcNow);

        Assert.Equal(WarehouseTransferStatus.Draft, header.Status);
        Assert.Null(header.CancelledAtUtc);
        Assert.Null(header.CancelledBy);
        Assert.Equal(ProductionTransferWorkflowStatus.Planned, link.WorkflowStatus);
        Assert.Equal(WarehouseTransferLineStatus.Open, header.Lines.Single().Status);
        Assert.False(ProductionWorkOrderTransferGrouping.IsUnlinkedProductionTransfer(link));
    }

    [Fact]
    public void HasNoPickedProgressForDraftLikeCancel_allows_started_released_header_without_picks()
    {
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Lines =
            [
                new WarehouseTransferLine { RequestedQuantity = 5, PickedQuantity = 0 },
            ],
            Tasks =
            [
                new WarehouseTransferTask
                {
                    TaskType = WarehouseTransferTaskType.Pick,
                    Status = WarehouseTransferTaskStatus.InProgress,
                    StartedBy = 1,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                },
            ],
        };

        Assert.True(ProductionTransferCancellationReturnRemainderSupport.HasNoPickedProgressForDraftLikeCancel(header));

        ProductionTransferCancellationReturnRemainderSupport.RevertZeroProgressHeaderToDraft(
            header,
            actor: 1,
            utcNow: DateTime.UtcNow);

        Assert.Equal(WarehouseTransferStatus.Draft, header.Status);
        Assert.Null(header.Tasks.Single().StartedBy);
        Assert.Equal(WarehouseTransferTaskStatus.InProgress, header.Tasks.Single().Status);
    }

    [Fact]
    public void HasNoPickedProgressForDraftLikeCancel_rejects_picked_or_awaiting_handover()
    {
        var picked = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Lines = [new WarehouseTransferLine { RequestedQuantity = 5, PickedQuantity = 2 }],
        };
        var handover = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.AwaitingHandover,
            Lines = [new WarehouseTransferLine { RequestedQuantity = 5, PickedQuantity = 0 }],
        };

        Assert.False(ProductionTransferCancellationReturnRemainderSupport.HasNoPickedProgressForDraftLikeCancel(picked));
        Assert.False(ProductionTransferCancellationReturnRemainderSupport.HasNoPickedProgressForDraftLikeCancel(handover));
    }
}
