using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferReturnMovementTests
{
    [Fact]
    public void BuildMovementLines_moves_from_staging_to_original_source_shelf()
    {
        const long stagingLocationId = 100;
        const long sourceLocationId = 200;
        var line = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 1,
            StockCodeSnapshot = "STK-1",
            UnitCode = "ADET",
            DefaultSourceLocationId = sourceLocationId,
            PickedQuantity = 5,
        };
        var header = new WarehouseTransferHeader
        {
            Id = 1,
            SourceWarehouseId = 1,
            SourceStagingLocationId = stagingLocationId,
            Lines = [line],
        };
        var task = new WarehouseTransferTask
        {
            Header = header,
            Lines =
            [
                new WarehouseTransferTaskLine
                {
                    Id = 501,
                    Line = line,
                    PlannedQuantity = 5,
                    ProcessedQuantity = 0,
                    SourceLocationId = stagingLocationId,
                    TargetLocationId = sourceLocationId,
                },
            ],
        };

        var rows = ProductionTransferReturnMovement.BuildMovementLines(task);

        var movement = Assert.Single(rows);
        Assert.Equal(stagingLocationId, movement.SourceLocationId);
        Assert.Equal(sourceLocationId, movement.TargetLocationId);
        Assert.Equal(5, movement.Quantity);
    }

    [Fact]
    public void BuildMovementLines_uses_tracking_staging_and_source_for_serial_lines()
    {
        const long stagingLocationId = 100;
        const long sourceLocationId = 200;
        var line = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 1,
            StockCodeSnapshot = "STK-1",
            UnitCode = "ADET",
            DefaultSourceLocationId = sourceLocationId,
            Trackings =
            [
                new WarehouseTransferTracking
                {
                    SerialNo = "SN-1",
                    PickedQuantity = 1,
                    SourceLocationId = sourceLocationId,
                    TargetLocationId = stagingLocationId,
                },
            ],
        };
        var header = new WarehouseTransferHeader
        {
            Id = 1,
            SourceWarehouseId = 1,
            SourceStagingLocationId = stagingLocationId,
        };
        var task = new WarehouseTransferTask
        {
            Header = header,
            Lines =
            [
                new WarehouseTransferTaskLine
                {
                    Id = 501,
                    Line = line,
                    PlannedQuantity = 1,
                    ProcessedQuantity = 0,
                    SourceLocationId = stagingLocationId,
                    TargetLocationId = sourceLocationId,
                },
            ],
        };

        var rows = ProductionTransferReturnMovement.BuildMovementLines(task);

        var movement = Assert.Single(rows);
        Assert.Equal(stagingLocationId, movement.SourceLocationId);
        Assert.Equal(sourceLocationId, movement.TargetLocationId);
        Assert.Equal("SN-1", movement.SerialNo);
    }

    [Fact]
    public void ResolveReturnTaskLineLocations_prefers_header_staging_and_original_source()
    {
        const long stagingLocationId = 100;
        const long sourceLocationId = 200;
        var header = new WarehouseTransferHeader { SourceStagingLocationId = stagingLocationId };
        var line = new WarehouseTransferLine
        {
            StockCodeSnapshot = "STK-1",
            DefaultSourceLocationId = sourceLocationId,
        };
        var pickTaskLine = new WarehouseTransferTaskLine { SourceLocationId = sourceLocationId };

        var (staging, target) = ProductionTransferReturnMovement.ResolveReturnTaskLineLocations(header, line, pickTaskLine);

        Assert.Equal(stagingLocationId, staging);
        Assert.Equal(sourceLocationId, target);
    }
}
