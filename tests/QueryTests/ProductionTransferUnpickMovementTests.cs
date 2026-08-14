using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferUnpickMovementTests
{
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
            activeTask, sourceTaskLine, line, 1, sourceLocationId: 300, actor: 7, utcNow: DateTime.UtcNow);

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
            activeTask, sourceTaskLine, line, 2, sourceLocationId: 300, actor: 7, utcNow: DateTime.UtcNow);

        Assert.NotSame(currentLine, reopened);
        Assert.Equal(2, activeTask.Lines.Count);
        Assert.Equal(2, reopened.PlannedQuantity);
        Assert.Equal(300, reopened.SourceLocationId);
        Assert.Equal(1, currentLine.PlannedQuantity);
        Assert.Equal(200, currentLine.SourceLocationId);
    }
}
