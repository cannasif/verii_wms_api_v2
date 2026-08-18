using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferLineSplitHelperTests
{
    [Fact]
    public void ResolveNextLineNoAnchor_uses_persisted_max_including_deleted_line_numbers()
    {
        var activeLines = new[]
        {
            new WarehouseTransferLine { LineNo = 1 },
        };

        Assert.Equal(2, ProductionTransferLineSplitHelper.ResolveNextLineNoAnchor(activeLines, persistedMax: 2));
        Assert.Equal(1, ProductionTransferLineSplitHelper.ResolveNextLineNoAnchor(activeLines, persistedMax: 0));
        Assert.Equal(3, ProductionTransferLineSplitHelper.ResolveNextLineNoAnchor(
            [new WarehouseTransferLine { LineNo = 3 }], persistedMax: 2));
    }

    [Fact]
    public void ConsolidateSameLocationOpenTaskLines_merges_same_stock_on_same_shelf()
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
                    RequiredQuantity = 2,
                },
                new()
                {
                    WarehouseTransferLineId = 11,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 3,
                },
            ],
        };
        const long a2 = 2;
        var lineA2Qty2 = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 4,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = a2,
            RequestedQuantity = 2,
            Trackings = [],
        };
        var lineA2Qty3 = new WarehouseTransferLine
        {
            Id = 11,
            LineNo = 8,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = a2,
            RequestedQuantity = 3,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [lineA2Qty2, lineA2Qty3] };
        var task = new WarehouseTransferTask
        {
            Lines =
            [
                new()
                {
                    Id = 100,
                    WtLineId = 10,
                    Line = lineA2Qty2,
                    PlannedQuantity = 2,
                    ProcessedQuantity = 0,
                    SourceLocationId = a2,
                },
                new()
                {
                    Id = 101,
                    WtLineId = 11,
                    Line = lineA2Qty3,
                    PlannedQuantity = 3,
                    ProcessedQuantity = 0,
                    SourceLocationId = a2,
                },
            ],
        };

        ProductionTransferLineSplitHelper.ConsolidateSameLocationOpenTaskLines(
            header,
            link,
            task,
            actor: 1,
            utcNow: DateTime.UtcNow);

        var activeTaskLines = task.Lines.Where(x => !x.IsDeleted).ToArray();
        Assert.Single(activeTaskLines);
        Assert.Equal(5, activeTaskLines[0].PlannedQuantity);
        Assert.Equal(5, lineA2Qty2.RequestedQuantity);
        Assert.True(lineA2Qty3.IsDeleted);
        Assert.True(link.Lines.Single(x => x.WarehouseTransferLineId == 11).IsDeleted);
        var mergedTaskLine = task.Lines.Single(x => x.WtLineId == 11);
        Assert.True(mergedTaskLine.IsDeleted);
        Assert.Equal(3, mergedTaskLine.PlannedQuantity);
    }

    [Fact]
    public void Full_unpick_of_three_completed_lines_to_same_shelf_merges_open_rows()
    {
        const long shelfA = 1;
        const long shelfB = 2;
        const long shelfC = 3;
        const long targetShelf = 10;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new() { WarehouseTransferLineId = 10, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 2 },
                new() { WarehouseTransferLineId = 11, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 3 },
                new() { WarehouseTransferLineId = 12, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 5 },
            ],
        };
        var lineA = new WarehouseTransferLine
        {
            Id = 10, LineNo = 1, StockId = 13, UnitCode = "ADET",
            DefaultSourceLocationId = shelfA, RequestedQuantity = 2, PickedQuantity = 2, Trackings = [],
        };
        var lineB = new WarehouseTransferLine
        {
            Id = 11, LineNo = 2, StockId = 13, UnitCode = "ADET",
            DefaultSourceLocationId = shelfB, RequestedQuantity = 3, PickedQuantity = 3, Trackings = [],
        };
        var lineC = new WarehouseTransferLine
        {
            Id = 12, LineNo = 3, StockId = 13, UnitCode = "ADET",
            DefaultSourceLocationId = shelfC, RequestedQuantity = 5, PickedQuantity = 5, Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [lineA, lineB, lineC] };
        var taskLineA = new WarehouseTransferTaskLine
        {
            Id = 100, WtLineId = 10, Line = lineA, PlannedQuantity = 2, ProcessedQuantity = 2, SourceLocationId = shelfA,
        };
        var taskLineB = new WarehouseTransferTaskLine
        {
            Id = 101, WtLineId = 11, Line = lineB, PlannedQuantity = 3, ProcessedQuantity = 3, SourceLocationId = shelfB,
        };
        var taskLineC = new WarehouseTransferTaskLine
        {
            Id = 102, WtLineId = 12, Line = lineC, PlannedQuantity = 5, ProcessedQuantity = 5, SourceLocationId = shelfC,
        };
        var task = new WarehouseTransferTask { Lines = [taskLineA, taskLineB, taskLineC] };
        var utcNow = DateTime.UtcNow;

        foreach (var (line, taskLine, quantity) in new[]
                 {
                     (lineA, taskLineA, 2m),
                     (lineB, taskLineB, 3m),
                     (lineC, taskLineC, 5m),
                 })
        {
            ProductionTransferUnpickMovement.ApplyUnpickedQuantities(line, taskLine, quantity, serialNo: null, actor: 1, utcNow);
            ProductionTransferUnpickMovement.ApplyUnpickedRouteLocations(line, taskLine, targetShelf, serialNo: null, actor: 1, utcNow);
            ProductionTransferLineSplitHelper.ConsolidateSameLocationOpenTaskLines(header, link, task, actor: 1, utcNow);
        }

        var activeTaskLines = task.Lines.Where(x => !x.IsDeleted).ToArray();
        Assert.Single(activeTaskLines);
        Assert.Equal(10, activeTaskLines[0].PlannedQuantity);
        Assert.Equal(0, activeTaskLines[0].ProcessedQuantity);
        Assert.Equal(targetShelf, activeTaskLines[0].SourceLocationId);
        Assert.Equal(10, lineA.RequestedQuantity);
        Assert.Equal(0, lineA.PickedQuantity);
        Assert.True(lineB.IsDeleted);
        Assert.True(lineC.IsDeleted);
        Assert.True(link.Lines.Single(x => x.WarehouseTransferLineId == 11).IsDeleted);
        Assert.True(link.Lines.Single(x => x.WarehouseTransferLineId == 12).IsDeleted);
    }

    [Fact]
    public void ConsolidateSameLocationPickedTaskLines_merges_fully_picked_rows_on_same_shelf()
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
                    RequiredQuantity = 2,
                },
                new()
                {
                    WarehouseTransferLineId = 11,
                    ProductionConsumptionId = 100,
                    RequirementReference = "REQ-1",
                    RequiredQuantity = 2,
                },
            ],
        };
        const long a1 = 68;
        var pickedLineA = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 2,
            PickedQuantity = 2,
            Trackings = [],
        };
        var pickedLineB = new WarehouseTransferLine
        {
            Id = 11,
            LineNo = 2,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 2,
            PickedQuantity = 2,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [pickedLineA, pickedLineB] };
        var task = new WarehouseTransferTask
        {
            Lines =
            [
                new()
                {
                    Id = 100,
                    WtLineId = 10,
                    Line = pickedLineA,
                    PlannedQuantity = 2,
                    ProcessedQuantity = 2,
                    SourceLocationId = a1,
                },
                new()
                {
                    Id = 101,
                    WtLineId = 11,
                    Line = pickedLineB,
                    PlannedQuantity = 2,
                    ProcessedQuantity = 2,
                    SourceLocationId = a1,
                },
            ],
        };

        var (keeperTaskLineId, keeperLineId) = ProductionTransferLineSplitHelper.ConsolidateSameLocationPickedTaskLines(
            header,
            link,
            task,
            actor: 1,
            utcNow: DateTime.UtcNow,
            focusTaskLineId: 101,
            focusLineId: 11);

        Assert.Equal(100, keeperTaskLineId);
        Assert.Equal(10, keeperLineId);
        var activeTaskLines = task.Lines.Where(x => !x.IsDeleted).ToArray();
        Assert.Single(activeTaskLines);
        Assert.Equal(4, activeTaskLines[0].PlannedQuantity);
        Assert.Equal(4, activeTaskLines[0].ProcessedQuantity);
        Assert.Equal(4, pickedLineA.RequestedQuantity);
        Assert.Equal(4, pickedLineA.PickedQuantity);
        Assert.True(pickedLineB.IsDeleted);
        Assert.True(link.Lines.Single(x => x.WarehouseTransferLineId == 11).IsDeleted);
    }

    [Fact]
    public void ApplyNonSerialRouteChunks_keeps_remainder_on_source_and_adds_routed_sibling()
    {
        const long a1 = 1;
        const long a2 = 2;
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
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            RequestedQuantity = 5,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 100,
            WtLineId = 10,
            Line = line,
            PlannedQuantity = 5,
            ProcessedQuantity = 0,
            SourceLocationId = a1,
        };
        var task = new WarehouseTransferTask { Lines = [taskLine] };
        var sourceLineLink = link.Lines.First();
        var nextLineNo = 1;
        var chunks = ProductionTransferRouteAllocation.BuildRouteRefreshSplitChunks(
            5,
            a1,
            [new RouteAllocationChunk(a2, 2, null, null)]);

        ProductionTransferLineSplitHelper.ApplyNonSerialRouteChunks(
            header,
            link,
            task,
            taskLine,
            line,
            sourceLineLink,
            chunks,
            ref nextLineNo,
            actor: 1,
            utcNow: DateTime.UtcNow);

        Assert.Equal(2, chunks.Length);
        Assert.Equal(a1, line.DefaultSourceLocationId);
        Assert.Equal(3, line.RequestedQuantity);
        Assert.Equal(3, taskLine.PlannedQuantity);
        var sibling = header.Lines.Single(x => x.Id != 10);
        Assert.Equal(a2, sibling.DefaultSourceLocationId);
        Assert.Equal(2, sibling.RequestedQuantity);
        var siblingTaskLine = task.Lines.Single(x => x.WtLineId == sibling.Id);
        Assert.Equal(2, siblingTaskLine.PlannedQuantity);
        Assert.Equal(a2, siblingTaskLine.SourceLocationId);
    }

    [Fact]
    public void ApplyNonSerialRouteChunks_keeps_unlocated_shortage_when_partially_routed()
    {
        const long a1 = 1;
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
            UnitCode = "ADET",
            DefaultSourceLocationId = null,
            RequestedQuantity = 5,
            Trackings = [],
        };
        var header = new WarehouseTransferHeader { Lines = [line] };
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 100,
            WtLineId = 10,
            Line = line,
            PlannedQuantity = 5,
            ProcessedQuantity = 0,
            SourceLocationId = null,
        };
        var task = new WarehouseTransferTask { Lines = [taskLine] };
        var sourceLineLink = link.Lines.First();
        var nextLineNo = 1;
        var chunks = ProductionTransferRouteAllocation.BuildRouteRefreshSplitChunks(
            5,
            currentSourceLocationId: null,
            [new RouteAllocationChunk(a1, 3, null, null)]);

        ProductionTransferLineSplitHelper.ApplyNonSerialRouteChunks(
            header,
            link,
            task,
            taskLine,
            line,
            sourceLineLink,
            chunks,
            ref nextLineNo,
            actor: 1,
            utcNow: DateTime.UtcNow,
            allowShortageWithoutLocation: true);

        Assert.Equal(a1, line.DefaultSourceLocationId);
        Assert.Equal(3, line.RequestedQuantity);
        Assert.Equal(3, taskLine.PlannedQuantity);
        Assert.Equal(a1, taskLine.SourceLocationId);
        var shortage = header.Lines.Single(x => x.Id != 10);
        Assert.Null(shortage.DefaultSourceLocationId);
        Assert.Equal(2, shortage.RequestedQuantity);
        var shortageTaskLine = task.Lines.Single(x => x.WtLineId == shortage.Id);
        Assert.Equal(2, shortageTaskLine.PlannedQuantity);
        Assert.Null(shortageTaskLine.SourceLocationId);
    }

    [Fact]
    public void RefreshSerialSources_skips_shortage_trackings_and_ignores_non_serial_balances()
    {
        const long serialShelf = 10;
        const long nonSerialShelf = 20;
        var line = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 13,
            UnitCode = "ADET",
            TrackingType = StockTrackingType.Serial,
            RequireSerial = true,
            Trackings =
            [
                new()
                {
                    PlannedQuantity = 1,
                    SerialNo = "SER-1",
                },
                new()
                {
                    PlannedQuantity = 1,
                    SerialNo = null,
                    SourceLocationId = nonSerialShelf,
                },
            ],
        };
        var taskLine = new WarehouseTransferTaskLine
        {
            WtLineId = 10,
            Line = line,
            PlannedQuantity = 2,
        };
        var context = new PickBalanceContext(
            [],
            new Dictionary<long, WarehouseLocation>
            {
                [serialShelf] = new() { Id = serialShelf, Code = "A-01" },
                [nonSerialShelf] = new() { Id = nonSerialShelf, Code = "B-01" },
            },
            [
                new()
                {
                    StockId = 13,
                    LocationId = serialShelf,
                    UnitCode = "ADET",
                    SerialNo = "SER-1",
                    AvailableQuantity = 1,
                },
                new()
                {
                    StockId = 13,
                    LocationId = nonSerialShelf,
                    UnitCode = "ADET",
                    SerialNo = null,
                    AvailableQuantity = 5,
                },
            ]);

        ProductionTransferLineSplitHelper.RefreshSerialSources(taskLine, line, context, actor: 1, utcNow: DateTime.UtcNow);

        var serialTracking = line.Trackings.Single(x => x.SerialNo == "SER-1");
        var shortageTracking = line.Trackings.Single(x => string.IsNullOrWhiteSpace(x.SerialNo));
        Assert.Equal(serialShelf, serialTracking.SourceLocationId);
        Assert.Null(shortageTracking.SourceLocationId);
    }

    [Fact]
    public void FindSerialTrackingBalance_returns_null_for_shortage_tracking()
    {
        var line = new WarehouseTransferLine
        {
            StockId = 13,
            UnitCode = "ADET",
            TrackingType = StockTrackingType.Serial,
            RequireSerial = true,
        };
        var tracking = new WarehouseTransferTracking { SerialNo = null, PlannedQuantity = 1 };
        var balance = new LocationStockBalance
        {
            StockId = 13,
            LocationId = 20,
            UnitCode = "ADET",
            SerialNo = null,
            AvailableQuantity = 5,
        };

        var result = ProductionTransferLineSplitHelper.FindSerialTrackingBalance(
            line,
            tracking,
            [balance],
            new Dictionary<long, WarehouseLocation> { [20] = new() { Id = 20, Code = "B-01" } });

        Assert.Null(result);
    }

    [Fact]
    public void ApplySerialShortageRouteChunks_moves_shortage_quantity_to_non_serial_siblings()
    {
        const long serialShelf = 10;
        const long bulkShelf = 20;
        var header = new WarehouseTransferHeader { BranchCode = "0", Lines = [] };
        var link = new ProductionTransferHeaderLink { BranchCode = "0", Lines = [] };
        var task = new WarehouseTransferTask { BranchCode = "0", Lines = [] };
        var line = new WarehouseTransferLine
        {
            Id = 10,
            LineNo = 1,
            StockId = 13,
            UnitCode = "ADET",
            TrackingType = StockTrackingType.Serial,
            RequireSerial = true,
            RequestedQuantity = 4,
            DefaultSourceLocationId = serialShelf,
            Trackings =
            [
                new() { Id = 1, PlannedQuantity = 1, SerialNo = "SER-1", SourceLocationId = serialShelf },
                new() { Id = 2, PlannedQuantity = 3, SerialNo = null },
            ],
        };
        header.Lines.Add(line);
        var sourceLineLink = new ProductionTransferLineLink
        {
            BranchCode = "0",
            WarehouseTransferLine = line,
            RequiredQuantity = 4,
        };
        link.Lines.Add(sourceLineLink);
        var taskLine = new WarehouseTransferTaskLine
        {
            Id = 100,
            BranchCode = "0",
            WtLineId = 10,
            Line = line,
            PlannedQuantity = 4,
            SourceLocationId = serialShelf,
        };
        task.Lines.Add(taskLine);
        var nextLineNo = 1;

        ProductionTransferLineSplitHelper.ApplySerialShortageRouteChunks(
            header,
            link,
            task,
            taskLine,
            line,
            sourceLineLink,
            [new(bulkShelf, 2, null, null), new(bulkShelf + 1, 1, null, null)],
            ref nextLineNo,
            actor: 1,
            utcNow: DateTime.UtcNow);

        Assert.Equal(1, line.RequestedQuantity);
        Assert.Equal(1, taskLine.PlannedQuantity);
        Assert.Equal(serialShelf, taskLine.SourceLocationId);
        Assert.True(line.Trackings.Single(x => x.SerialNo == "SER-1").PlannedQuantity > 0);
        Assert.True(line.Trackings.Single(x => string.IsNullOrWhiteSpace(x.SerialNo)).IsDeleted);
        Assert.Equal(2, header.Lines.Count(x => x.Id != 10));
        Assert.All(header.Lines.Where(x => x.Id != 10), sibling => Assert.Empty(sibling.Trackings));
    }

    [Fact]
    public void AssignSerialToShortage_splits_multi_quantity_shortage_and_adds_serial_tracking()
    {
        const long a2 = 20;
        var line = new WarehouseTransferLine
        {
            Id = 10,
            BranchCode = "0",
            StockId = 13,
            UnitCode = "ADET",
            TrackingType = StockTrackingType.Serial,
            RequireSerial = true,
            Trackings =
            [
                new() { Id = 1, PlannedQuantity = 1, SerialNo = "SER-1", SourceLocationId = 10 },
                new() { Id = 2, PlannedQuantity = 4, SerialNo = null },
            ],
        };
        var taskLine = new WarehouseTransferTaskLine
        {
            WtLineId = 10,
            Line = line,
            PlannedQuantity = 5,
            SourceLocationId = 10,
        };

        ProductionTransferLineSplitHelper.AssignSerialToShortage(
            line, taskLine, a2, "SER-NEW", null, actor: 1, utcNow: DateTime.UtcNow);

        var shortage = line.Trackings.Single(x => string.IsNullOrWhiteSpace(x.SerialNo));
        var assigned = line.Trackings.Single(x => x.SerialNo == "SER-NEW");
        Assert.Equal(3, shortage.PlannedQuantity);
        Assert.Equal(a2, assigned.SourceLocationId);
        Assert.Equal(1, assigned.PlannedQuantity);
    }

    [Fact]
    public void AssignSerialToShortage_converts_single_quantity_shortage_tracking_in_place()
    {
        var line = new WarehouseTransferLine
        {
            Id = 10,
            BranchCode = "0",
            StockId = 13,
            UnitCode = "ADET",
            TrackingType = StockTrackingType.Serial,
            RequireSerial = true,
            Trackings =
            [
                new() { Id = 2, PlannedQuantity = 1, SerialNo = null },
            ],
        };
        var taskLine = new WarehouseTransferTaskLine { WtLineId = 10, Line = line, PlannedQuantity = 1 };

        ProductionTransferLineSplitHelper.AssignSerialToShortage(
            line, taskLine, 20, "SER-NEW", null, actor: 1, utcNow: DateTime.UtcNow);

        var tracking = Assert.Single(line.Trackings);
        Assert.Equal("SER-NEW", tracking.SerialNo);
        Assert.Equal(20, tracking.SourceLocationId);
        Assert.Equal(1, tracking.PlannedQuantity);
    }
}
