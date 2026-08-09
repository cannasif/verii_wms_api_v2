using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionWorkOrderTransferGroupingTests
{
    [Fact]
    public void ApplyTabFilter_picking_includes_cancelled_with_open_cancellation_return()
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

        Assert.True(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking, header, link));
        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Cancelled, header, link));
    }

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
        const long originPickTaskId = 100;
        const long kalanPickTaskId = 200;
        var link = new ProductionTransferHeaderLink { WarehouseTransferHeaderId = 1, ProductionOrderNo = "WO-2" };
        var context = new ProductionWorkOrderTransferGrouping.LabelContext();
        var kalanTask = new WarehouseTransferTask
        {
            Id = kalanPickTaskId,
            TaskNo = "TR-1-2",
            TaskType = WarehouseTransferTaskType.Pick,
            Status = WarehouseTransferTaskStatus.Open,
            PreviousTaskId = originPickTaskId,
            Assignments = []
        };
        var tasks = new WarehouseTransferTask[]
        {
            kalanTask,
            new()
            {
                Id = originPickTaskId,
                TaskNo = "TR-1-1",
                TaskType = WarehouseTransferTaskType.Pick,
                Status = WarehouseTransferTaskStatus.Completed,
                Assignments = []
            },
            new()
            {
                TaskType = WarehouseTransferTaskType.AssignmentReturn,
                Status = WarehouseTransferTaskStatus.Completed,
                OriginTaskId = originPickTaskId
            }
        };

        var suffix = ProductionWorkOrderTransferGrouping.GetDisplaySuffix(kalanTask, link, context, tasks);

        Assert.Equal("-KALANTRANSFER", suffix);
        Assert.Equal("TR-1-KALANTRANSFER", ProductionWorkOrderTransferGrouping.BuildDisplayLabel(
            kalanTask.TaskNo, "TR-1", suffix));
    }

    [Fact]
    public void ResolveAssignedUsernames_returns_historical_when_active_assignment_removed()
    {
        const long userA = 10;
        var users = new Dictionary<long, string> { [userA] = "ali" };
        var task = new WarehouseTransferTask
        {
            Assignments =
            [
                new WarehouseTransferTaskAssignment
                {
                    UserId = userA,
                    IsDeleted = true,
                    AssignedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                }
            ]
        };

        var usernames = ProductionWorkOrderTransferGrouping.ResolveAssignedUsernames(task.Assignments, users);

        Assert.Equal(["ali"], usernames);
    }

    [Fact]
    public void ResolveAssignedUsernames_prefers_active_over_historical()
    {
        const long userA = 10;
        const long userB = 20;
        var users = new Dictionary<long, string> { [userA] = "ali", [userB] = "veli" };
        var task = new WarehouseTransferTask
        {
            Assignments =
            [
                new WarehouseTransferTaskAssignment
                {
                    UserId = userA,
                    IsDeleted = true,
                    AssignedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                },
                new WarehouseTransferTaskAssignment
                {
                    UserId = userB,
                    IsDeleted = false,
                    IsPrimary = true,
                    AssignedAtUtc = DateTimeOffset.UtcNow,
                }
            ]
        };

        var usernames = ProductionWorkOrderTransferGrouping.ResolveAssignedUsernames(task.Assignments, users);

        Assert.Equal(["veli"], usernames);
    }

    private static bool MatchesMyAssignments(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        long userId) =>
        ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking, header, link)
        && header.Tasks.Any(task => ProductionWorkOrderTransferGrouping.HasActionableAssignmentForUser(task, userId));

    [Fact]
    public void MyAssignments_shows_only_transfers_with_current_user_assignment_in_picking()
    {
        const long userA = 10;
        const long userB = 20;
        var pickingHeader = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Assignments = [new WarehouseTransferTaskAssignment { UserId = userA, IsDeleted = false }]
                }
            ]
        };
        var otherHeader = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Assignments = [new WarehouseTransferTaskAssignment { UserId = userB, IsDeleted = false }]
                }
            ]
        };
        var unassignedHeader = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Tasks = [new WarehouseTransferTask { Assignments = [] }]
        };
        var link = new ProductionTransferHeaderLink { WorkflowStatus = ProductionTransferWorkflowStatus.Picking };

        Assert.True(MatchesMyAssignments(pickingHeader, link, userA));
        Assert.False(MatchesMyAssignments(otherHeader, link, userA));
        Assert.False(MatchesMyAssignments(unassignedHeader, link, userA));
    }

    [Fact]
    public void MyAssignments_excludes_completed_workflow_transfers()
    {
        const long userA = 10;
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Assignments = [new WarehouseTransferTaskAssignment { UserId = userA, IsDeleted = false }]
                }
            ]
        };
        var link = new ProductionTransferHeaderLink
        {
            WorkflowStatus = ProductionTransferWorkflowStatus.Completed
        };

        Assert.False(MatchesMyAssignments(header, link, userA));
    }

    [Fact]
    public void MyAssignments_includes_cancelled_with_open_cancellation_return_when_assigned()
    {
        const long userA = 10;
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Cancelled,
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Assignments = [new WarehouseTransferTaskAssignment { UserId = userA, IsDeleted = false }]
                },
                new WarehouseTransferTask
                {
                    TaskType = WarehouseTransferTaskType.CancellationReturn,
                    Status = WarehouseTransferTaskStatus.InProgress
                }
            ]
        };
        var link = new ProductionTransferHeaderLink { WorkflowStatus = ProductionTransferWorkflowStatus.Cancelled };

        Assert.True(MatchesMyAssignments(header, link, userA));
    }

    [Fact]
    public void MyAssignments_excludes_transfer_when_user_only_has_completed_task_assignment()
    {
        const long userA = 10;
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Id = 1,
                    Status = WarehouseTransferTaskStatus.Completed,
                    Assignments = [new WarehouseTransferTaskAssignment { UserId = userA, IsDeleted = false }]
                },
                new WarehouseTransferTask
                {
                    Id = 2,
                    Status = WarehouseTransferTaskStatus.Open,
                    Assignments = []
                }
            ]
        };
        var link = new ProductionTransferHeaderLink { WorkflowStatus = ProductionTransferWorkflowStatus.Picking };

        Assert.False(MatchesMyAssignments(header, link, userA));
    }

    [Fact]
    public void MyAssignments_excludes_completed_assignment_return_even_when_assignment_remains()
    {
        const long userA = 10;
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Id = 1,
                    TaskType = WarehouseTransferTaskType.AssignmentReturn,
                    Status = WarehouseTransferTaskStatus.Completed,
                    Assignments = [new WarehouseTransferTaskAssignment { UserId = userA, IsDeleted = false }]
                },
                new WarehouseTransferTask
                {
                    Id = 2,
                    TaskType = WarehouseTransferTaskType.Pick,
                    Status = WarehouseTransferTaskStatus.Open,
                    PreviousTaskId = 1,
                    Assignments = []
                }
            ]
        };
        var link = new ProductionTransferHeaderLink { WorkflowStatus = ProductionTransferWorkflowStatus.Picking };

        Assert.False(MatchesMyAssignments(header, link, userA));
    }

    [Fact]
    public void HasActionableAssignmentForUser_is_true_for_in_progress_pick()
    {
        const long userA = 10;
        var task = new WarehouseTransferTask
        {
            Status = WarehouseTransferTaskStatus.InProgress,
            Assignments = [new WarehouseTransferTaskAssignment { UserId = userA, IsDeleted = false }]
        };

        Assert.True(ProductionWorkOrderTransferGrouping.HasActionableAssignmentForUser(task, userA));
    }
}
