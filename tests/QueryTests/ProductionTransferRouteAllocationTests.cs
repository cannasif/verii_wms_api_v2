using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferRouteAllocationTests
{
    [Fact]
    public void GetSiblingCommittedSourceLocationIds_excludes_locations_assigned_to_other_split_rows()
    {
        const long a1 = 1;
        const long a2 = 2;
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new() { WarehouseTransferLineId = 10, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 2 },
                new() { WarehouseTransferLineId = 11, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 3 },
            ],
        };
        var lineA1 = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = a1,
            Trackings = [],
        };
        var lineA2 = new WarehouseTransferLine
        {
            Id = 11,
            StockId = 13,
            UnitCode = "ADET",
            DefaultSourceLocationId = a2,
            Trackings = [],
        };
        var task = new WarehouseTransferTask
        {
            Lines =
            [
                new()
                {
                    Id = 1000,
                    WtLineId = 10,
                    Line = lineA1,
                    PlannedQuantity = 2,
                    ProcessedQuantity = 0,
                    SourceLocationId = a1,
                },
                new()
                {
                    Id = 1001,
                    WtLineId = 11,
                    Line = lineA2,
                    PlannedQuantity = 3,
                    ProcessedQuantity = 0,
                    SourceLocationId = a2,
                },
            ],
        };

        var excludedForA2 = ProductionTransferRouteAllocation.GetSiblingCommittedSourceLocationIds(
            task,
            task.Lines.Single(x => x.Id == 1001),
            lineA2,
            link);

        Assert.Contains(a1, excludedForA2);
        Assert.DoesNotContain(a2, excludedForA2);
    }

    [Fact]
    public void AllocateGreedyNonSerial_uses_only_eligible_locations_after_excluding_sibling_commitments()
    {
        const long a1 = 1;
        const long a2 = 2;
        var locations = new Dictionary<long, verii_wms_api_v2.Modules.Location.Domain.WarehouseLocation>
        {
            [a1] = new() { Id = a1, Code = "A1" },
            [a2] = new() { Id = a2, Code = "A2" },
        };
        var balances = new[]
        {
            Balance(a1, 13, 2),
            Balance(a2, 13, 30),
        };

        var eligible = ProductionTransferRouteAllocation.ExcludeLocations(balances, new HashSet<long> { a1 });
        var chunks = ProductionTransferRouteAllocation.AllocateGreedyNonSerial(
            3, 13, null, "ADET", eligible, locations);

        Assert.Equal(3, chunks.Sum(x => x.Quantity));
        Assert.All(chunks, chunk => Assert.Equal(a2, chunk.LocationId));
    }

    [Fact]
    public void ListSerialRouteRefreshCandidates_excludes_current_and_assigned_serials()
    {
        const long a1 = 1;
        const long a2 = 2;
        var locations = new Dictionary<long, verii_wms_api_v2.Modules.Location.Domain.WarehouseLocation>
        {
            [a1] = new() { Id = a1, Code = "A1" },
            [a2] = new() { Id = a2, Code = "A2" },
        };
        var balances = new[]
        {
            SerialBalance(a1, 13, "UTG-1", 1),
            SerialBalance(a2, 13, "UTG-2", 1),
            SerialBalance(a2, 13, "UTG-5", 1),
        };

        var candidates = ProductionTransferRouteAllocation.ListSerialRouteRefreshCandidates(
            13,
            null,
            "ADET",
            "UTG-1",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UTG-2" },
            balances,
            locations);

        Assert.Single(candidates);
        Assert.Equal("UTG-5", candidates[0].SerialNo);
        Assert.Equal(a2, candidates[0].LocationId);
    }

    [Fact]
    public void GetAssignedSerialNumbersInGroup_collects_open_serials_in_same_requirement_group()
    {
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new() { WarehouseTransferLineId = 10, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 3 },
            ],
        };
        var line = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 13,
            UnitCode = "ADET",
            Trackings =
            [
                new() { SerialNo = "UTG-1", PlannedQuantity = 1, PickedQuantity = 0 },
                new() { SerialNo = "UTG-2", PlannedQuantity = 1, PickedQuantity = 0 },
            ],
        };
        var task = new WarehouseTransferTask
        {
            Lines =
            [
                new()
                {
                    Id = 1000,
                    WtLineId = 10,
                    Line = line,
                    PlannedQuantity = 2,
                    ProcessedQuantity = 0,
                },
            ],
        };

        var assigned = ProductionTransferRouteAllocation.GetAssignedSerialNumbersInGroup(
            task,
            line,
            link,
            exceptSerialNo: "UTG-1");

        Assert.Single(assigned);
        Assert.Contains("UTG-2", assigned);
    }

    private static verii_wms_api_v2.Modules.StockBalance.Domain.LocationStockBalance SerialBalance(
        long locationId,
        long stockId,
        string serialNo,
        decimal quantity) => new()
    {
        LocationId = locationId,
        StockId = stockId,
        UnitCode = "ADET",
        SerialNo = serialNo,
        AvailableQuantity = quantity,
        StockStatus = "Available",
    };

    private static verii_wms_api_v2.Modules.StockBalance.Domain.LocationStockBalance Balance(
        long locationId,
        long stockId,
        decimal quantity) => new()
    {
        LocationId = locationId,
        StockId = stockId,
        UnitCode = "ADET",
        AvailableQuantity = quantity,
        StockStatus = "Available",
    };
}
