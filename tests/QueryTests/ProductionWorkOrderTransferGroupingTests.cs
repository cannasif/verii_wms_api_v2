using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionWorkOrderTransferGroupingTests
{
    [Fact]
    public void MatchesTab_completed_uses_workflow_status()
    {
        var header = new WarehouseTransferHeader { Status = WarehouseTransferStatus.Released };
        var link = new ProductionTransferHeaderLink { WorkflowStatus = ProductionTransferWorkflowStatus.Completed };

        Assert.True(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Completed, header, link));
        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking, header, link));
    }

    [Fact]
    public void MatchesTab_cancelled_hides_while_cancellation_return_is_open()
    {
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Cancelled,
            Tasks =
            [
                new WarehouseTransferTask
                {
                    TaskType = WarehouseTransferTaskType.CancellationReturn,
                    Status = WarehouseTransferTaskStatus.InProgress
                }
            ]
        };
        var link = new ProductionTransferHeaderLink { WorkflowStatus = ProductionTransferWorkflowStatus.Cancelled };

        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Cancelled, header, link));
        Assert.True(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking, header, link));
    }

    [Fact]
    public void BuildLabelContext_marks_partial_and_current_kalan_headers()
    {
        const long rootId = 10;
        const long residualId = 20;
        var links = new[]
        {
            new ProductionTransferHeaderLink
            {
                WarehouseTransferHeaderId = rootId,
                ProductionOrderNo = "WO-1",
                WorkflowStatus = ProductionTransferWorkflowStatus.CompletedWithShortage,
                ResidualWarehouseTransferHeaderId = residualId
            },
            new ProductionTransferHeaderLink
            {
                WarehouseTransferHeaderId = residualId,
                ProductionOrderNo = "WO-1",
                ParentWarehouseTransferHeaderId = rootId,
                WorkflowStatus = ProductionTransferWorkflowStatus.Picking
            }
        };

        var context = ProductionWorkOrderTransferGrouping.BuildLabelContext(links);

        Assert.Equal(1, context.PartialTransferIndex[rootId]);
        Assert.Contains(residualId, context.CurrentKalanHeaderIds);
    }

    [Fact]
    public void GetDisplaySuffix_uses_kalan_after_completed_assignment_return()
    {
        const long pickTaskId = 100;
        var link = new ProductionTransferHeaderLink { WarehouseTransferHeaderId = 1, ProductionOrderNo = "WO-2" };
        var context = new ProductionWorkOrderTransferGrouping.LabelContext();
        var pickTask = new WarehouseTransferTask
        {
            Id = pickTaskId,
            TaskNo = "TR-1-1",
            TaskType = WarehouseTransferTaskType.Pick,
            Status = WarehouseTransferTaskStatus.Open,
            Assignments = []
        };
        var tasks = new WarehouseTransferTask[]
        {
            pickTask,
            new()
            {
                TaskType = WarehouseTransferTaskType.AssignmentReturn,
                Status = WarehouseTransferTaskStatus.Completed,
                OriginTaskId = pickTaskId
            }
        };

        var suffix = ProductionWorkOrderTransferGrouping.GetDisplaySuffix(pickTask, link, context, tasks);

        Assert.Equal("-KALANTRANSFER", suffix);
        Assert.Equal("TR-1-KALANTRANSFER", ProductionWorkOrderTransferGrouping.BuildDisplayLabel(
            pickTask.TaskNo, "TR-1", suffix));
    }
}
