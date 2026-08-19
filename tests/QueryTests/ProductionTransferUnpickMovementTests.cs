using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferUnpickMovementTests
{
    [Fact]
    public void ResolveStagingLocationId_uses_waiting_location_not_planned_tracking_target()
    {
        const long waitingLocationId = 100;
        const long plannedTargetLocationId = 900;
        const long sourceLocationId = 68;
        var tracking = new WarehouseTransferTracking
        {
            SerialNo = "QWR-3",
            PickedQuantity = 1,
            SourceLocationId = sourceLocationId,
            TargetLocationId = plannedTargetLocationId,
        };
        var line = new WarehouseTransferLine
        {
            Id = 10,
            DefaultTargetLocationId = plannedTargetLocationId,
            Trackings = [tracking],
        };
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 1676,
            WtLineId = 10,
            TargetLocationId = waitingLocationId,
            Line = line,
        };
        var header = new WarehouseTransferHeader
        {
            SourceStagingLocationId = waitingLocationId,
            Lines = [line],
        };

        var staging = ProductionTransferUnpickMovement.ResolveStagingLocationId(
            header, line, taskLine, tracking);

        Assert.Equal(waitingLocationId, staging);
        Assert.NotEqual(plannedTargetLocationId, staging);
    }

    [Fact]
    public void BuildMovementLine_moves_from_staging_to_selected_shelf()
    {
        const long stagingLocationId = 100;
        const long targetLocationId = 300;
        var line = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 1,
            StockCodeSnapshot = "STK-1",
            UnitCode = "ADET",
        };
        var header = new WarehouseTransferHeader
        {
            Id = 1,
            SourceWarehouseId = 1,
            SourceStagingLocationId = stagingLocationId,
            Lines = [line],
        };

        var movement = ProductionTransferUnpickMovement.BuildMovementLine(
            header, line, stagingLocationId, targetLocationId, 4, null, "SN-1");

        Assert.Equal(stagingLocationId, movement.SourceLocationId);
        Assert.Equal(targetLocationId, movement.TargetLocationId);
        Assert.Equal(4, movement.Quantity);
        Assert.Equal("SN-1", movement.SerialNo);
    }

    [Fact]
    public void ApplyUnpickedQuantities_reduces_processed_and_picked_amounts()
    {
        var line = new WarehouseTransferLine
        {
            Id = 10,
            RequestedQuantity = 10,
            PickedQuantity = 6,
            Status = WarehouseTransferLineStatus.PartiallyPicked,
            Trackings =
            [
                new WarehouseTransferTracking
                {
                    SerialNo = "SN-1",
                    PlannedQuantity = 1,
                    PickedQuantity = 1,
                    Status = WarehouseTransferTrackingStatus.Picked,
                },
            ],
        };
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            PlannedQuantity = 6,
            ProcessedQuantity = 6,
            Line = line,
        };

        ProductionTransferUnpickMovement.ApplyUnpickedQuantities(
            line, taskLine, 1, "SN-1", actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(5, line.PickedQuantity);
        Assert.Equal(5, taskLine.ProcessedQuantity);
        Assert.Equal(0, line.Trackings.Single().PickedQuantity);
        Assert.Equal(WarehouseTransferTrackingStatus.Planned, line.Trackings.Single().Status);
    }

    [Fact]
    public void NetBarcodeAcceptedQuantity_subtracts_unpick_journal_entries()
    {
        var scans = new[]
        {
            new ProductionTransferBarcodeScan
            {
                NormalizedBarcode = "LBL-1",
                BarcodeSource = "Label",
                Quantity = 5,
            },
            new ProductionTransferBarcodeScan
            {
                NormalizedBarcode = "LBL-1",
                BarcodeSource = ProductionTransferUnpickMovement.BarcodeSource,
                Quantity = 2,
            },
        };

        var net = ProductionTransferUnpickMovement.NetBarcodeAcceptedQuantity(scans, "LBL-1");

        Assert.Equal(3, net);
    }

    [Fact]
    public void ApplyPartialUnpickSplit_keeps_picked_portion_and_adds_open_sibling()
    {
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new()
                {
                    WarehouseTransferLineId = 10,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 5,
                },
            ],
        };
        var line = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = 200,
            RequestedQuantity = 5,
            PickedQuantity = 3,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = 10,
            Line = line,
            PlannedQuantity = 5,
            ProcessedQuantity = 3,
            SourceLocationId = 200,
        };
        var task = new WarehouseTransferTask { Lines = [taskLine] };
        var lineLink = link.Lines.First();
        var nextLineNo = 1;

        ProductionTransferLineSplitHelper.ApplyPartialUnpickSplit(
            header, link, task, taskLine, line, lineLink, 2, 68, ref nextLineNo, actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(3, taskLine.PlannedQuantity);
        Assert.Equal(3, taskLine.ProcessedQuantity);
        Assert.Equal(3, line.RequestedQuantity);
        Assert.Equal(2, header.Lines.Count);
        var siblingTaskLine = Assert.Single(task.Lines, x => x.Id != 501);
        Assert.Equal(2, siblingTaskLine.PlannedQuantity);
        Assert.Equal(0, siblingTaskLine.ProcessedQuantity);
        Assert.Equal(68, siblingTaskLine.SourceLocationId);
        Assert.Equal(0, taskLine.PlannedQuantity - taskLine.ProcessedQuantity);
        Assert.Equal(2, siblingTaskLine.PlannedQuantity - siblingTaskLine.ProcessedQuantity);
    }

    [Fact]
    public void ApplyPartialUnpickSplit_preserves_source_open_remainder_when_returning_to_different_shelf()
    {
        const long a1 = 68;
        const long b1 = 99;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new()
                {
                    WarehouseTransferLineId = 10,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 10,
                },
            ],
        };
        var line = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 10,
            PickedQuantity = 4,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = 10,
            Line = line,
            PlannedQuantity = 10,
            ProcessedQuantity = 4,
            SourceLocationId = a1,
        };
        var task = new WarehouseTransferTask { Lines = [taskLine] };
        var lineLink = link.Lines.First();
        var nextLineNo = 1;

        ProductionTransferLineSplitHelper.ApplyPartialUnpickSplit(
            header, link, task, taskLine, line, lineLink, 2, b1, ref nextLineNo, actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(8, taskLine.PlannedQuantity);
        Assert.Equal(4, taskLine.ProcessedQuantity);
        Assert.Equal(8, line.RequestedQuantity);
        Assert.Equal(a1, taskLine.SourceLocationId);
        Assert.Equal(2, header.Lines.Count);
        Assert.Equal(2, task.Lines.Count(x => !x.IsDeleted));

        var targetOpenTaskLine = Assert.Single(task.Lines, x => !x.IsDeleted && x.Id != 501);
        Assert.Equal(2, targetOpenTaskLine.PlannedQuantity);
        Assert.Equal(0, targetOpenTaskLine.ProcessedQuantity);
        Assert.Equal(b1, targetOpenTaskLine.SourceLocationId);
        Assert.Equal(4, taskLine.PlannedQuantity - taskLine.ProcessedQuantity);
    }

    [Fact]
    public void ApplyPartialUnpickSplit_keeps_source_open_remainder_after_full_unpick_to_different_shelf()
    {
        const long a1 = 68;
        const long a11 = 99;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new()
                {
                    WarehouseTransferLineId = 10,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 2,
                },
                new()
                {
                    WarehouseTransferLineId = 11,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 26,
                },
            ],
        };
        var a1Line = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 2,
            PickedQuantity = 0,
            Trackings = [],
        };
        var a11OpenLine = new WarehouseTransferLine
        {
            Id = 11,
            LineNo = 2,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a11,
            RequestedQuantity = 26,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [a1Line, a11OpenLine] };
        var a1TaskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = 10,
            Line = a1Line,
            PlannedQuantity = 2,
            ProcessedQuantity = 0,
            SourceLocationId = a1,
            TargetLocationId = 100,
        };
        var a11TaskLine = new WarehouseTransferTaskLine
        {
            Id = 502,
            WtLineId = 11,
            Line = a11OpenLine,
            PlannedQuantity = 26,
            ProcessedQuantity = 0,
            SourceLocationId = a11,
        };
        var task = new WarehouseTransferTask { Lines = [a1TaskLine, a11TaskLine] };
        var a1LineLink = link.Lines.First(x => x.WarehouseTransferLineId == 10);
        var nextLineNo = 2;

        ProductionTransferLineSplitHelper.ApplyPartialUnpickSplit(
            header, link, task, a1TaskLine, a1Line, a1LineLink, 1, a11, ref nextLineNo, actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(1, a1TaskLine.PlannedQuantity);
        Assert.Equal(0, a1TaskLine.ProcessedQuantity);
        Assert.Equal(a1, a1TaskLine.SourceLocationId);
        Assert.Null(a1TaskLine.TargetLocationId);
        Assert.Equal(27, a11TaskLine.PlannedQuantity);
        Assert.Equal(27, a11OpenLine.RequestedQuantity);
        Assert.Equal(2, header.Lines.Count(x => !x.IsDeleted));
        Assert.Equal(2, task.Lines.Count(x => !x.IsDeleted));
    }

    [Fact]
    public void ApplyPartialUnpickSplit_creates_target_sibling_when_full_unpick_cannot_merge()
    {
        const long a1 = 68;
        const long a11 = 99;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new()
                {
                    WarehouseTransferLineId = 10,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 2,
                },
            ],
        };
        var a1Line = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 2,
            PickedQuantity = 0,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [a1Line] };
        var a1TaskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = 10,
            Line = a1Line,
            PlannedQuantity = 2,
            ProcessedQuantity = 0,
            SourceLocationId = a1,
        };
        var task = new WarehouseTransferTask { Lines = [a1TaskLine] };
        var a1LineLink = link.Lines.First();
        var nextLineNo = 1;

        ProductionTransferLineSplitHelper.ApplyPartialUnpickSplit(
            header, link, task, a1TaskLine, a1Line, a1LineLink, 1, a11, ref nextLineNo, actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(1, a1TaskLine.PlannedQuantity);
        Assert.Equal(a1, a1TaskLine.SourceLocationId);
        Assert.Equal(2, header.Lines.Count);
        var a11TaskLine = Assert.Single(task.Lines, x => x.Id != 501);
        Assert.Equal(1, a11TaskLine.PlannedQuantity);
        Assert.Equal(0, a11TaskLine.ProcessedQuantity);
        Assert.Equal(a11, a11TaskLine.SourceLocationId);
    }

    [Fact]
    public void ShouldSplitUnpickAcrossLocations_when_full_unpick_leaves_open_remainder_on_source()
    {
        var taskLine = new WarehouseTransferTaskLine
        {
            PlannedQuantity = 2,
            ProcessedQuantity = 0,
            SourceLocationId = 68,
        };

        Assert.True(ProductionTransferLineSplitHelper.ShouldSplitUnpickAcrossLocations(
            taskLine, sourceLocationId: 68, targetLocationId: 99, unpickedQuantity: 1));
        Assert.False(ProductionTransferLineSplitHelper.ShouldSplitUnpickAcrossLocations(
            taskLine, sourceLocationId: 68, targetLocationId: 68, unpickedQuantity: 1));
        Assert.False(ProductionTransferLineSplitHelper.ShouldSplitUnpickAcrossLocations(
            taskLine, sourceLocationId: 68, targetLocationId: 99, unpickedQuantity: 2));
    }

    [Fact]
    public void ApplyPartialUnpickSplit_merges_source_remainder_into_existing_open_sibling_when_returning_to_different_shelf()
    {
        const long a1 = 68;
        const long b1 = 99;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new()
                {
                    WarehouseTransferLineId = 10,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 8,
                },
                new()
                {
                    WarehouseTransferLineId = 11,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 4,
                },
            ],
        };
        var pickedLine = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 8,
            PickedQuantity = 4,
            Trackings = [],
        };
        var openLine = new WarehouseTransferLine
        {
            Id = 11,
            LineNo = 2,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 4,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [pickedLine, openLine] };
        var pickedTaskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = 10,
            Line = pickedLine,
            PlannedQuantity = 8,
            ProcessedQuantity = 4,
            SourceLocationId = a1,
        };
        var openTaskLine = new WarehouseTransferTaskLine
        {
            Id = 502,
            WtLineId = 11,
            Line = openLine,
            PlannedQuantity = 4,
            ProcessedQuantity = 0,
            SourceLocationId = a1,
        };
        var task = new WarehouseTransferTask { Lines = [pickedTaskLine, openTaskLine] };
        var pickedLineLink = link.Lines.First(x => x.WarehouseTransferLineId == 10);
        var nextLineNo = 2;

        ProductionTransferLineSplitHelper.ApplyPartialUnpickSplit(
            header, link, task, pickedTaskLine, pickedLine, pickedLineLink, 2, b1, ref nextLineNo, actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(4, pickedTaskLine.PlannedQuantity);
        Assert.Equal(4, pickedTaskLine.ProcessedQuantity);
        Assert.Equal(4, pickedLine.RequestedQuantity);
        Assert.Equal(6, openTaskLine.PlannedQuantity);
        Assert.Equal(6, openLine.RequestedQuantity);
        Assert.Equal(3, header.Lines.Count);
        Assert.Equal(3, task.Lines.Count(x => !x.IsDeleted));
        var targetOpenTaskLine = Assert.Single(task.Lines, x => !x.IsDeleted && x.SourceLocationId == b1);
        Assert.Equal(2, targetOpenTaskLine.PlannedQuantity);
        Assert.Equal(0, targetOpenTaskLine.ProcessedQuantity);
    }

    [Fact]
    public void ApplyPartialUnpickSplit_merges_into_existing_open_sibling_at_same_location()
    {
        const long a1 = 68;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new()
                {
                    WarehouseTransferLineId = 10,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 6,
                },
                new()
                {
                    WarehouseTransferLineId = 11,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 4,
                },
            ],
        };
        var pickedLine = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 6,
            PickedQuantity = 5,
            Trackings = [],
        };
        var openLine = new WarehouseTransferLine
        {
            Id = 11,
            LineNo = 2,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 4,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [pickedLine, openLine] };
        var pickedTaskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = 10,
            Line = pickedLine,
            PlannedQuantity = 6,
            ProcessedQuantity = 5,
            SourceLocationId = a1,
        };
        var openTaskLine = new WarehouseTransferTaskLine
        {
            Id = 502,
            WtLineId = 11,
            Line = openLine,
            PlannedQuantity = 4,
            ProcessedQuantity = 0,
            SourceLocationId = a1,
        };
        var task = new WarehouseTransferTask { Lines = [pickedTaskLine, openTaskLine] };
        var pickedLineLink = link.Lines.First(x => x.WarehouseTransferLineId == 10);
        var nextLineNo = 2;

        ProductionTransferLineSplitHelper.ApplyPartialUnpickSplit(
            header, link, task, pickedTaskLine, pickedLine, pickedLineLink, 1, a1, ref nextLineNo, actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(5, pickedTaskLine.PlannedQuantity);
        Assert.Equal(5, pickedTaskLine.ProcessedQuantity);
        Assert.Equal(5, openTaskLine.PlannedQuantity);
        Assert.Equal(5, openLine.RequestedQuantity);
        Assert.Equal(2, header.Lines.Count);
        Assert.Equal(2, task.Lines.Count(x => !x.IsDeleted));
    }

    [Fact]
    public void ApplyPartialUnpickSplit_preserves_open_remainder_on_same_task_line()
    {
        const long a1 = 68;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new()
                {
                    WarehouseTransferLineId = 10,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 10,
                },
            ],
        };
        var line = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            StockCodeSnapshot = "ASD",
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 10,
            PickedQuantity = 5,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = 10,
            Line = line,
            PlannedQuantity = 10,
            ProcessedQuantity = 5,
            SourceLocationId = a1,
        };
        var task = new WarehouseTransferTask { Lines = [taskLine] };
        var lineLink = link.Lines.First();
        var nextLineNo = 1;

        ProductionTransferLineSplitHelper.ApplyPartialUnpickSplit(
            header, link, task, taskLine, line, lineLink, 1, a1, ref nextLineNo, actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(10, taskLine.PlannedQuantity);
        Assert.Equal(5, taskLine.ProcessedQuantity);
        Assert.Equal(10, line.RequestedQuantity);
        Assert.Single(header.Lines);
        Assert.Single(task.Lines);
    }

    [Fact]
    public void ReopenTransferredQuantity_merges_into_open_sibling_at_same_location()
    {
        const long a1 = 68;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new()
                {
                    WarehouseTransferLineId = 10,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 6,
                },
                new()
                {
                    WarehouseTransferLineId = 11,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 4,
                },
            ],
        };
        var pickedLine = new WarehouseTransferLine
        {
            Id = 10,
            TrackingType = StockTrackingType.None,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
        };
        var openLine = new WarehouseTransferLine
        {
            Id = 11,
            TrackingType = StockTrackingType.None,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 4,
        };
        var header = new WarehouseTransferHeader { Lines = [pickedLine, openLine] };
        var sourceTaskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = pickedLine.Id,
            Line = pickedLine,
        };
        var openTaskLine = new WarehouseTransferTaskLine
        {
            Id = 502,
            WtLineId = openLine.Id,
            Line = openLine,
            PlannedQuantity = 4,
            ProcessedQuantity = 0,
            SourceLocationId = a1,
        };
        var activeTask = new WarehouseTransferTask
        {
            Id = 3,
            BranchCode = "01",
            Lines = [openTaskLine],
        };

        var reopened = ProductionTransferUnpickMovement.ReopenTransferredQuantityInActiveTask(
            header,
            link,
            activeTask,
            sourceTaskLine,
            pickedLine,
            link.Lines.First(x => x.WarehouseTransferLineId == 10),
            1,
            sourceLocationId: a1,
            actor: 7,
            utcNow: DateTime.UtcNow);

        Assert.Same(openTaskLine, reopened);
        Assert.Single(activeTask.Lines);
        Assert.Equal(5, openTaskLine.PlannedQuantity);
        Assert.Equal(5, openLine.RequestedQuantity);
    }

    [Fact]
    public void ApplyUnpickedRouteLocations_updates_source_to_target_shelf()
    {
        const long targetLocationId = 300;
        var line = new WarehouseTransferLine { Id = 10, DefaultSourceLocationId = 200 };
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            SourceLocationId = 200,
            TargetLocationId = 100,
            Line = line,
        };
        line.Trackings.Add(new WarehouseTransferTracking
        {
            SerialNo = "SN-1",
            PlannedQuantity = 1,
            PickedQuantity = 0,
            SourceLocationId = 200,
            TargetLocationId = 100,
        });

        ProductionTransferUnpickMovement.ApplyUnpickedRouteLocations(
            line, taskLine, targetLocationId, "SN-1", actor: 7, utcNow: DateTime.UtcNow);

        Assert.Equal(targetLocationId, line.DefaultSourceLocationId);
        Assert.Equal(targetLocationId, taskLine.SourceLocationId);
        Assert.Null(taskLine.TargetLocationId);
        Assert.Equal(targetLocationId, line.Trackings.Single().SourceLocationId);
        Assert.Null(line.Trackings.Single().TargetLocationId);
    }

    [Fact]
    public void ReopenTransferredQuantity_adds_serial_pick_back_to_current_task()
    {
        var line = new WarehouseTransferLine
        {
            Id = 10,
            TrackingType = StockTrackingType.Serial,
        };
        var sourceTaskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = line.Id,
            Line = line,
            PlannedQuantity = 1,
            ProcessedQuantity = 0,
        };
        var currentLine = new WarehouseTransferTaskLine
        {
            Id = 503,
            WtLineId = line.Id,
            Line = line,
            PlannedQuantity = 1,
            ProcessedQuantity = 0,
        };
        var activeTask = new WarehouseTransferTask
        {
            Id = 3,
            BranchCode = "01",
            Lines = [currentLine],
        };

        var reopened = ProductionTransferUnpickMovement.ReopenTransferredQuantityInActiveTask(
            new WarehouseTransferHeader { Lines = [line] },
            new ProductionTransferHeaderLink
            {
                Lines =
                [
                    new()
                    {
                        WarehouseTransferLineId = line.Id,
                        ProductionConsumptionId = 1,
                        RequirementReference = "REQ-1",
                        RequiredQuantity = 1,
                    },
                ],
            },
            activeTask,
            sourceTaskLine,
            line,
            new ProductionTransferLineLink
            {
                WarehouseTransferLineId = line.Id,
                ProductionConsumptionId = 1,
                RequirementReference = "REQ-1",
                RequiredQuantity = 1,
            },
            1,
            sourceLocationId: 300,
            actor: 7,
            utcNow: DateTime.UtcNow);

        Assert.Same(currentLine, reopened);
        Assert.Equal(2, currentLine.PlannedQuantity);
        Assert.Single(activeTask.Lines);
    }

    [Fact]
    public void ReopenTransferredQuantity_keeps_non_serial_source_shelves_separate()
    {
        var line = new WarehouseTransferLine
        {
            Id = 10,
            TrackingType = StockTrackingType.None,
            DefaultSourceLocationId = 200,
        };
        var sourceTaskLine = new WarehouseTransferTaskLine
        {
            Id = 501,
            WtLineId = line.Id,
            Line = line,
        };
        var currentLine = new WarehouseTransferTaskLine
        {
            Id = 503,
            WtLineId = line.Id,
            Line = line,
            PlannedQuantity = 1,
            SourceLocationId = 200,
        };
        var activeTask = new WarehouseTransferTask
        {
            Id = 3,
            BranchCode = "01",
            Lines = [currentLine],
        };

        var reopened = ProductionTransferUnpickMovement.ReopenTransferredQuantityInActiveTask(
            new WarehouseTransferHeader { Lines = [line] },
            new ProductionTransferHeaderLink
            {
                Lines =
                [
                    new()
                    {
                        WarehouseTransferLineId = line.Id,
                        ProductionConsumptionId = 1,
                        RequirementReference = "REQ-1",
                        RequiredQuantity = 1,
                    },
                ],
            },
            activeTask,
            sourceTaskLine,
            line,
            new ProductionTransferLineLink
            {
                WarehouseTransferLineId = line.Id,
                ProductionConsumptionId = 1,
                RequirementReference = "REQ-1",
                RequiredQuantity = 1,
            },
            2,
            sourceLocationId: 300,
            actor: 7,
            utcNow: DateTime.UtcNow);

        Assert.NotSame(currentLine, reopened);
        Assert.Equal(2, activeTask.Lines.Count);
        Assert.Equal(2, reopened.PlannedQuantity);
        Assert.Equal(300, reopened.SourceLocationId);
        Assert.Equal(1, currentLine.PlannedQuantity);
        Assert.Equal(200, currentLine.SourceLocationId);
    }
}
