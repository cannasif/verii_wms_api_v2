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

    [Fact]
    public void BuildCancellationReturnDisplayLabel_uses_iptaliade_suffix()
    {
        Assert.Equal(
            "TR-100-IPTALIADE",
            ProductionWorkOrderTransferGrouping.BuildCancellationReturnDisplayLabel("TR-100-IPTALIADE1", "TR-100"));
    }

    [Fact]
    public void IsUnassignedCancellationReturnTask_is_true_for_open_unassigned_iptaliade()
    {
        var task = new WarehouseTransferTask
        {
            TaskNo = "TR-100-IPTALIADE1",
            TaskType = WarehouseTransferTaskType.CancellationReturn,
            Status = WarehouseTransferTaskStatus.Open,
            Assignments = []
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsProductionCancellationReturnTask(task));
    }

    [Fact]
    public void IsPostCancellationReturnUnassignedPickTask_links_to_completed_iptaliade()
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
            Assignments = []
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsPostCancellationReturnUnassignedPickTask(
            kalan,
            [iptaliade, kalan]));
    }

    [Fact]
    public void IsPostCancellationReturnUnassignedPickTask_links_to_completed_warehouse_cancel_return()
    {
        var cancelReturn = new WarehouseTransferTask
        {
            Id = 50,
            TaskNo = "TR-100-C01",
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
            Assignments = []
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsPostCancellationReturnUnassignedPickTask(
            kalan,
            [cancelReturn, kalan]));
    }

    [Fact]
    public void MatchesTab_excludes_transfer_with_only_iptal_kalan_remainder_from_picking_and_cancelled()
    {
        // Atanmayan bu tür kalan görevler artık Picking/Cancelled sekmelerinde değil,
        // ayrı Atanmayanlar listesinde (bkz. ResolveAtanmayanlarListingKey/IsAtanmayanlarUnassignedPickTask) gösteriliyor.
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
            Assignments = []
        };
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Released,
            Tasks = [iptaliade, kalan]
        };
        var link = new ProductionTransferHeaderLink { WorkflowStatus = ProductionTransferWorkflowStatus.Picking };

        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking, header, link));

        var cancelledHeader = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Cancelled,
            Tasks = [iptaliade, kalan]
        };
        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Cancelled, cancelledHeader, link));
    }

    [Fact]
    public void MatchesTab_cancelled_hides_unlinked_transfer_with_only_iptal_kalan_remainder()
    {
        var cancelReturn = new WarehouseTransferTask
        {
            Id = 50,
            TaskNo = "TR-100-C01",
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
            Assignments = []
        };
        var header = new WarehouseTransferHeader
        {
            DocumentNo = "TR-100",
            Status = WarehouseTransferStatus.Cancelled,
            Tasks = [cancelReturn, kalan]
        };
        var link = new ProductionTransferHeaderLink
        {
            ProductionOrderId = null,
            ProductionHeaderId = null,
            ProductionOrderNo = null,
            WorkflowStatus = ProductionTransferWorkflowStatus.Picking
        };

        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Cancelled, header, link));
        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking, header, link));
    }

    [Fact]
    public void MatchesTab_cancelled_includes_cancelled_work_order_linked_transfer_without_open_kalan()
    {
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Cancelled,
            Tasks =
            [
                new WarehouseTransferTask
                {
                    TaskType = WarehouseTransferTaskType.CancellationReturn,
                    Status = WarehouseTransferTaskStatus.Completed,
                }
            ]
        };
        var link = new ProductionTransferHeaderLink
        {
            ProductionOrderNo = "WO-1",
            WorkflowStatus = ProductionTransferWorkflowStatus.Cancelled
        };

        Assert.True(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Cancelled, header, link));
    }

    [Fact]
    public void IsUnlinkedReleasedDraftPickTask_matches_marked_open_pick_without_assignments()
    {
        var link = new ProductionTransferHeaderLink
        {
            ProductionOrderId = null,
            ProductionHeaderId = null,
            ProductionOrderNo = null,
        };
        var task = new WarehouseTransferTask
        {
            TaskType = WarehouseTransferTaskType.Pick,
            Status = WarehouseTransferTaskStatus.Open,
            Description = "TR-200 iptal sonrası atanmaya açık toplama işi.",
            Assignments = []
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsUnlinkedReleasedDraftPickTask(task, link));
        Assert.True(ProductionWorkOrderTransferGrouping.IsAtanmayanlarUnassignedPickTask(task, link, [task]));
    }

    [Fact]
    public void ResolveAtanmayanlarListingKey_uses_document_no_for_unlinked_transfers()
    {
        var header = new WarehouseTransferHeader { DocumentNo = "TR-300", ExternalReferenceNo = null };
        var link = new ProductionTransferHeaderLink
        {
            ProductionOrderId = null,
            ProductionHeaderId = null,
            ProductionOrderNo = null,
            WarehouseTransferHeader = header,
        };

        Assert.Equal("TR-300", ProductionWorkOrderTransferGrouping.ResolveAtanmayanlarListingKey(link, header));
    }

    [Fact]
    public void IsPostShortageHandoverUnassignedPickTask_matches_auto_generated_child_transfer()
    {
        var link = new ProductionTransferHeaderLink
        {
            ParentWarehouseTransferHeaderId = 10,
            AutoGenerated = true,
        };
        var task = new WarehouseTransferTask
        {
            TaskNo = "TR-200-P01",
            TaskType = WarehouseTransferTaskType.Pick,
            Status = WarehouseTransferTaskStatus.Open,
            Assignments = []
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsPostShortageHandoverUnassignedPickTask(task, link));
    }

    [Fact]
    public void IsPostShortageHandoverUnassignedPickTask_is_false_without_parent_link()
    {
        var link = new ProductionTransferHeaderLink { AutoGenerated = true };
        var task = new WarehouseTransferTask
        {
            TaskType = WarehouseTransferTaskType.Pick,
            Status = WarehouseTransferTaskStatus.Open,
            Assignments = []
        };

        Assert.False(ProductionWorkOrderTransferGrouping.IsPostShortageHandoverUnassignedPickTask(task, link));
    }

    [Fact]
    public void IsOpenPartialTransferRemainderLink_is_true_for_open_auto_generated_child_transfer()
    {
        var link = new ProductionTransferHeaderLink
        {
            ParentWarehouseTransferHeaderId = 10,
            AutoGenerated = true,
            WorkflowStatus = ProductionTransferWorkflowStatus.Planned,
            WarehouseTransferHeader = new WarehouseTransferHeader
            {
                Status = WarehouseTransferStatus.Released,
            }
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsOpenPartialTransferRemainderLink(link));
    }

    [Fact]
    public void MatchesTab_picking_excludes_open_partial_transfer_remainder()
    {
        var header = new WarehouseTransferHeader { Status = WarehouseTransferStatus.Released };
        var link = new ProductionTransferHeaderLink
        {
            ParentWarehouseTransferHeaderId = 10,
            AutoGenerated = true,
            WorkflowStatus = ProductionTransferWorkflowStatus.Planned,
            WarehouseTransferHeader = header,
        };

        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking, header, link));
    }

    [Fact]
    public void FilterActiveOpenPartialTransferRemainderLinks_keeps_only_remainders_from_latest_manual_assignment()
    {
        var transfer1 = CreateManualLink(10, created: new DateTime(2026, 1, 1));
        var kalanA = CreateOpenKalanLink(11, parentHeaderId: 10);
        var transfer2 = CreateManualLink(20, created: new DateTime(2026, 2, 1));
        var kalanB = CreateOpenKalanLink(21, parentHeaderId: 20);

        var active = ProductionWorkOrderTransferGrouping.FilterActiveOpenPartialTransferRemainderLinks(
            [transfer1, kalanA, transfer2, kalanB]);

        Assert.Single(active);
        Assert.Equal(21, active[0].WarehouseTransferHeaderId);
    }

    [Fact]
    public void FilterActiveOpenPartialTransferRemainderLinks_keeps_nested_remainder_in_same_lineage()
    {
        var transfer1 = CreateManualLink(10, created: new DateTime(2026, 1, 1));
        var kalanA = CreateOpenKalanLink(11, parentHeaderId: 10);
        kalanA.WorkflowStatus = ProductionTransferWorkflowStatus.CompletedWithShortage;
        var kalanA2 = CreateOpenKalanLink(12, parentHeaderId: 11);

        var active = ProductionWorkOrderTransferGrouping.FilterActiveOpenPartialTransferRemainderLinks(
            [transfer1, kalanA, kalanA2]);

        Assert.Single(active);
        Assert.Equal(12, active[0].WarehouseTransferHeaderId);
    }

    private static ProductionTransferHeaderLink CreateManualLink(long headerId, DateTime created) =>
        new()
        {
            AutoGenerated = false,
            WarehouseTransferHeaderId = headerId,
            WorkflowStatus = ProductionTransferWorkflowStatus.CompletedWithShortage,
            WarehouseTransferHeader = new WarehouseTransferHeader
            {
                Id = headerId,
                CreatedDate = created,
                Status = WarehouseTransferStatus.Completed,
            },
        };

    private static ProductionTransferHeaderLink CreateOpenKalanLink(long headerId, long parentHeaderId) =>
        new()
        {
            AutoGenerated = true,
            ParentWarehouseTransferHeaderId = parentHeaderId,
            WarehouseTransferHeaderId = headerId,
            WorkflowStatus = ProductionTransferWorkflowStatus.Planned,
            WarehouseTransferHeader = new WarehouseTransferHeader
            {
                Id = headerId,
                Status = WarehouseTransferStatus.Released,
            },
        };

    [Fact]
    public void GetOrderKey_falls_back_to_transfer_id_when_work_order_missing()
    {
        var link = new ProductionTransferHeaderLink { WarehouseTransferHeaderId = 42 };

        Assert.Equal("transfer:42", ProductionWorkOrderTransferGrouping.GetOrderKey(link));
    }

    [Fact]
    public void GetOrderKey_keeps_distinct_keys_for_unlinked_manual_transfers()
    {
        var first = new ProductionTransferHeaderLink { WarehouseTransferHeaderId = 10 };
        var second = new ProductionTransferHeaderLink { WarehouseTransferHeaderId = 20 };

        Assert.NotEqual(
            ProductionWorkOrderTransferGrouping.GetOrderKey(first),
            ProductionWorkOrderTransferGrouping.GetOrderKey(second));
    }

    [Fact]
    public void BuildLabelContext_supports_manual_transfers_without_work_order_reference()
    {
        var links = new[]
        {
            new ProductionTransferHeaderLink
            {
                WarehouseTransferHeaderId = 101,
                WorkflowStatus = ProductionTransferWorkflowStatus.Picking,
            },
            new ProductionTransferHeaderLink
            {
                WarehouseTransferHeaderId = 202,
                WorkflowStatus = ProductionTransferWorkflowStatus.Picking,
            },
        };

        var context = ProductionWorkOrderTransferGrouping.BuildLabelContext(links);

        Assert.Empty(context.PartialTransferIndex);
        Assert.Empty(context.CurrentKalanHeaderIds);
    }

    [Fact]
    public void BuildDisplayLabel_uses_document_no_for_manual_transfer_task_suffix()
    {
        var label = ProductionWorkOrderTransferGrouping.BuildDisplayLabel("PT-55-1", "PT-55", "-KALANTRANSFER");

        Assert.Equal("PT-55-KALANTRANSFER", label);
    }

    [Fact]
    public void GetDisplaySuffix_does_not_throw_for_manual_transfer_pick_task()
    {
        var link = new ProductionTransferHeaderLink { WarehouseTransferHeaderId = 77 };
        var task = new WarehouseTransferTask
        {
            TaskType = WarehouseTransferTaskType.Pick,
            Status = WarehouseTransferTaskStatus.Assigned,
        };
        var context = ProductionWorkOrderTransferGrouping.BuildLabelContext([link]);

        var suffix = ProductionWorkOrderTransferGrouping.GetDisplaySuffix(task, link, context, [task]);

        Assert.Null(suffix);
    }
}
