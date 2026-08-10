using Microsoft.AspNetCore.Http;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferPickingSupportTests
{
    [Fact]
    public void ResolveWorkerPickTask_returns_assigned_active_task()
    {
        const long workerId = 10;
        var header = new WarehouseTransferHeader
        {
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Id = 1,
                    TaskType = WarehouseTransferTaskType.Pick,
                    Status = WarehouseTransferTaskStatus.InProgress,
                    Assignments =
                    [
                        new WarehouseTransferTaskAssignment { UserId = workerId, IsDeleted = false },
                    ],
                },
            ],
        };

        var task = ProductionTransferPickingSupport.ResolveWorkerPickTask(header, workerId);

        Assert.Equal(1, task.Id);
    }

    [Fact]
    public void ResolveActivePickTaskForResume_prefers_assigned_non_completed_task()
    {
        const long workerId = 10;
        var header = new WarehouseTransferHeader
        {
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Id = 1,
                    TaskType = WarehouseTransferTaskType.Pick,
                    Status = WarehouseTransferTaskStatus.Assigned,
                    Assignments =
                    [
                        new WarehouseTransferTaskAssignment { UserId = workerId, IsDeleted = false },
                    ],
                },
            ],
        };

        var task = ProductionTransferPickingSupport.ResolveActivePickTaskForResume(header, workerId);

        Assert.Equal(1, task.Id);
    }

    [Fact]
    public void ResolveWorkerPickTask_rejects_user_without_assignment_after_handoff()
    {
        const long previousWorkerId = 10;
        const long nextWorkerId = 20;
        var header = new WarehouseTransferHeader
        {
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Id = 1,
                    TaskType = WarehouseTransferTaskType.Pick,
                    Status = WarehouseTransferTaskStatus.Completed,
                    Assignments =
                    [
                        new WarehouseTransferTaskAssignment { UserId = previousWorkerId, IsDeleted = false },
                    ],
                },
                new WarehouseTransferTask
                {
                    Id = 2,
                    TaskType = WarehouseTransferTaskType.Pick,
                    Status = WarehouseTransferTaskStatus.InProgress,
                    PreviousTaskId = 1,
                    Assignments =
                    [
                        new WarehouseTransferTaskAssignment { UserId = nextWorkerId, IsDeleted = false },
                    ],
                },
            ],
        };

        var exception = Assert.Throws<AppException>(() =>
            ProductionTransferPickingSupport.ResolveWorkerPickTask(header, previousWorkerId));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
    }

    [Fact]
    public void ResolveAssignedPickTaskForLine_rejects_line_from_unassigned_task()
    {
        const long previousWorkerId = 10;
        const long nextWorkerId = 20;
        const long taskLineId = 501;
        var header = new WarehouseTransferHeader
        {
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Id = 2,
                    TaskType = WarehouseTransferTaskType.Pick,
                    Status = WarehouseTransferTaskStatus.InProgress,
                    Assignments =
                    [
                        new WarehouseTransferTaskAssignment { UserId = nextWorkerId, IsDeleted = false },
                    ],
                    Lines =
                    [
                        new WarehouseTransferTaskLine { Id = taskLineId, IsDeleted = false },
                    ],
                },
            ],
        };

        var exception = Assert.Throws<AppException>(() =>
            ProductionTransferPickingSupport.ResolveAssignedPickTaskForLine(header, taskLineId, previousWorkerId));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
    }

    [Fact]
    public void BuildRecipeRows_returns_one_row_per_recipe_line_without_location()
    {
        var lineA = new WarehouseTransferLine
        {
            Id = 101,
            LineNo = 1,
            StockId = 10,
            StockCodeSnapshot = "A",
            StockNameSnapshot = "Ürün A",
            TrackingType = StockTrackingType.None,
        };
        var lineB = new WarehouseTransferLine
        {
            Id = 102,
            LineNo = 2,
            StockId = 11,
            StockCodeSnapshot = "B",
            StockNameSnapshot = "Ürün B",
            TrackingType = StockTrackingType.Serial,
        };
        var header = new WarehouseTransferHeader
        {
            Lines = [lineA, lineB],
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Lines =
                    [
                        new WarehouseTransferTaskLine
                        {
                            Id = 501,
                            WtLineId = 101,
                            PlannedQuantity = 5,
                            ProcessedQuantity = 0,
                            Line = lineA,
                        },
                        new WarehouseTransferTaskLine
                        {
                            Id = 502,
                            WtLineId = 102,
                            PlannedQuantity = 2,
                            ProcessedQuantity = 0,
                            Line = lineB,
                        },
                    ],
                },
            ],
        };
        var task = header.Tasks.First();

        var rows = ProductionTransferPickingSupport.BuildRecipeRows(header, task);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Null(row.SourceLocationId);
            Assert.Null(row.SourceLocationCode);
            Assert.Null(row.SerialNo);
            Assert.False(row.CanPick);
        });
        Assert.Equal(5, rows[0].RequestedQuantity);
        Assert.Equal(2, rows[1].RequestedQuantity);
    }

    [Fact]
    public void BuildPersistedRows_after_handoff_hides_parent_picked_serials_from_child_task()
    {
        const long locationId = 9001;
        var line = new WarehouseTransferLine
        {
            Id = 102,
            LineNo = 1,
            StockId = 11,
            StockCodeSnapshot = "ASD",
            StockNameSnapshot = "Ürün ASD",
            TrackingType = StockTrackingType.Serial,
            PickedQuantity = 1,
            DefaultSourceLocationId = locationId,
            Trackings =
            [
                new WarehouseTransferTracking { Id = 1, SerialNo = "SN-1", PlannedQuantity = 1, PickedQuantity = 1, SourceLocationId = locationId },
                new WarehouseTransferTracking { Id = 2, SerialNo = "SN-2", PlannedQuantity = 1, PickedQuantity = 0, SourceLocationId = locationId },
                new WarehouseTransferTracking { Id = 3, SerialNo = "SN-3", PlannedQuantity = 1, PickedQuantity = 0, SourceLocationId = locationId },
                new WarehouseTransferTracking { Id = 4, SerialNo = "SN-4", PlannedQuantity = 1, PickedQuantity = 0, SourceLocationId = locationId },
                new WarehouseTransferTracking { Id = 5, SerialNo = "SN-5", PlannedQuantity = 1, PickedQuantity = 0, SourceLocationId = locationId },
            ],
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var childTask = new WarehouseTransferTask
        {
            Id = 2,
            PreviousTaskId = 1,
            Lines =
            [
                new WarehouseTransferTaskLine
                {
                    Id = 502,
                    WtLineId = 102,
                    PlannedQuantity = 4,
                    ProcessedQuantity = 0,
                    Line = line,
                },
            ],
        };
        var locationCodes = new Dictionary<long, string> { [locationId] = "A1" };

        var rows = ProductionTransferPickingSupport.BuildPersistedRows(header, childTask, locationCodes);

        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Equal(0, row.ProcessedQuantity));
        Assert.DoesNotContain(rows, row => row.SerialNo == "SN-1");
        Assert.Equal(["SN-2", "SN-3", "SN-4", "SN-5"], rows.Select(x => x.SerialNo).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void BuildPersistedRows_keeps_partially_picked_serials_on_active_task()
    {
        const long locationId = 9001;
        var line = new WarehouseTransferLine
        {
            Id = 102,
            LineNo = 1,
            StockId = 11,
            StockCodeSnapshot = "ASD",
            TrackingType = StockTrackingType.Serial,
            PickedQuantity = 1,
            DefaultSourceLocationId = locationId,
            Trackings =
            [
                new WarehouseTransferTracking { Id = 1, SerialNo = "SN-1", PlannedQuantity = 1, PickedQuantity = 1, SourceLocationId = locationId },
                new WarehouseTransferTracking { Id = 2, SerialNo = "SN-2", PlannedQuantity = 1, PickedQuantity = 0, SourceLocationId = locationId },
            ],
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var task = new WarehouseTransferTask
        {
            Lines =
            [
                new WarehouseTransferTaskLine
                {
                    Id = 501,
                    WtLineId = 102,
                    PlannedQuantity = 5,
                    ProcessedQuantity = 1,
                    Line = line,
                },
            ],
        };
        var locationCodes = new Dictionary<long, string> { [locationId] = "A1" };

        var rows = ProductionTransferPickingSupport.BuildPersistedRows(header, task, locationCodes);

        Assert.Equal(2, rows.Count);
        var picked = Assert.Single(rows.Where(x => x.SerialNo == "SN-1"));
        Assert.Equal(1, picked.ProcessedQuantity);
        Assert.Equal(0, picked.RemainingQuantity);
        var open = Assert.Single(rows.Where(x => x.SerialNo == "SN-2"));
        Assert.Equal(0, open.ProcessedQuantity);
        Assert.Equal(1, open.RemainingQuantity);
    }

    [Fact]
    public void BuildPersistedRows_splits_partially_picked_non_serial_into_open_and_completed_rows()
    {
        const long locationId = 9001;
        var line = new WarehouseTransferLine
        {
            Id = 101,
            LineNo = 1,
            StockId = 10,
            StockCodeSnapshot = "ASD",
            StockNameSnapshot = "Ürün ASD",
            TrackingType = StockTrackingType.None,
            PickedQuantity = 4,
            DefaultSourceLocationId = locationId,
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var task = new WarehouseTransferTask
        {
            Lines =
            [
                new WarehouseTransferTaskLine
                {
                    Id = 501,
                    WtLineId = 101,
                    PlannedQuantity = 5,
                    ProcessedQuantity = 4,
                    SourceLocationId = locationId,
                    Line = line,
                },
            ],
        };
        var locationCodes = new Dictionary<long, string> { [locationId] = "A1" };

        var rows = ProductionTransferPickingSupport.BuildPersistedRows(header, task, locationCodes);

        Assert.Equal(2, rows.Count);
        var picked = Assert.Single(rows.Where(x => x.ProcessedQuantity > 0));
        Assert.Equal(4, picked.ProcessedQuantity);
        Assert.Equal(0, picked.RemainingQuantity);
        var open = Assert.Single(rows.Where(x => x.RemainingQuantity > 0));
        Assert.Equal(0, open.ProcessedQuantity);
        Assert.Equal(1, open.RemainingQuantity);
        Assert.True(open.CanPick);
    }

    [Fact]
    public void SortDisplayRows_keeps_route_split_rows_after_anchor_line()
    {
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new() { WarehouseTransferLineId = 10, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 2 },
                new() { WarehouseTransferLineId = 11, ProductionConsumptionId = 101, RequirementReference = "REQ-2", RequiredQuantity = 1 },
                new() { WarehouseTransferLineId = 12, ProductionConsumptionId = 102, RequirementReference = "REQ-3", RequiredQuantity = 1 },
                new() { WarehouseTransferLineId = 13, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 3 },
                new() { WarehouseTransferLineId = 17, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 3 },
            ],
        };
        var header = new WarehouseTransferHeader
        {
            Lines =
            [
                new() { Id = 10, LineNo = 1, StockId = 1, UnitCode = "ADET" },
                new() { Id = 11, LineNo = 2, StockId = 2, UnitCode = "ADET" },
                new() { Id = 12, LineNo = 3, StockId = 3, UnitCode = "ADET" },
                new() { Id = 13, LineNo = 4, StockId = 4, UnitCode = "ADET" },
                new() { Id = 14, LineNo = 5, StockId = 4, UnitCode = "ADET" },
                new() { Id = 15, LineNo = 6, StockId = 5, UnitCode = "ADET" },
                new() { Id = 16, LineNo = 7, StockId = 6, UnitCode = "ADET" },
                new() { Id = 17, LineNo = 8, StockId = 4, UnitCode = "ADET", DefaultSourceLocationId = 2 },
            ],
        };
        var rows = new List<ProductionTransferPickingRowDto>
        {
            new(1001, 10, 1, 1, "A1", 1, "S1", null, null, 1, 1, 0, true),
            new(1002, 11, 2, 2, "A2", 2, "S2", null, null, 1, 1, 0, true),
            new(1003, 12, 3, 3, "A3", 3, "S3", null, null, 1, 1, 0, true),
            new(1004, 13, 4, 1, "A1", 4, "ASD", null, null, 2, 2, 0, true),
            new(1005, 17, 8, 2, "A2", 4, "ASD", null, null, 3, 3, 0, true),
            new(1006, 14, 5, 1, "A1", 5, "S5", null, null, 1, 1, 0, true),
            new(1007, 15, 6, 1, "A1", 6, "S6", null, null, 1, 1, 0, true),
            new(1008, 16, 7, 1, "A1", 7, "S7", null, null, 1, 1, 0, true),
        };

        var sorted = ProductionTransferPickingSupport.SortDisplayRows(rows, header, link);

        Assert.Equal([1, 2, 3, 4, 8, 5, 6, 7], sorted.Select(x => x.LineNo).ToArray());
    }
}
