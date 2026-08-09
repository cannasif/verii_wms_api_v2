using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferLineSplitHelperTests
{
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
}
