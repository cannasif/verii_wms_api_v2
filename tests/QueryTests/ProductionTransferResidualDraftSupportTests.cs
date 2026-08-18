using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public class ProductionTransferResidualDraftSupportTests
{
    [Fact]
    public void ResolveResidualSourceLocationId_uses_open_pick_task_line_when_line_default_is_null()
    {
        const long wtLineId = 10;
        const long sourceA = 101;
        var header = new WarehouseTransferHeader
        {
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Id = 1,
                    TaskType = WarehouseTransferTaskType.Pick,
                    Lines =
                    [
                        new WarehouseTransferTaskLine
                        {
                            WtLineId = wtLineId,
                            PlannedQuantity = 10,
                            ProcessedQuantity = 0,
                            SourceLocationId = sourceA,
                        },
                    ],
                },
            ],
        };
        var line = new WarehouseTransferLine
        {
            Id = wtLineId,
            DefaultSourceLocationId = null,
        };

        var resolved = ProductionTransferResidualDraftSupport.ResolveResidualSourceLocationId(
            header,
            line,
            ProductionTransferResidualDraftSupport.ResolvePrimaryPickTask(header));

        Assert.Equal(sourceA, resolved);
    }

    [Fact]
    public void ResolveResidualSourceLocationId_uses_processed_pick_task_line_when_no_open_remainder()
    {
        const long wtLineId = 12;
        const long sourceC = 303;
        var header = new WarehouseTransferHeader
        {
            Tasks =
            [
                new WarehouseTransferTask
                {
                    Id = 2,
                    TaskType = WarehouseTransferTaskType.Pick,
                    Lines =
                    [
                        new WarehouseTransferTaskLine
                        {
                            WtLineId = wtLineId,
                            PlannedQuantity = 6,
                            ProcessedQuantity = 6,
                            SourceLocationId = sourceC,
                        },
                    ],
                },
            ],
        };
        var line = new WarehouseTransferLine
        {
            Id = wtLineId,
            DefaultSourceLocationId = null,
        };

        var resolved = ProductionTransferResidualDraftSupport.ResolveResidualSourceLocationId(
            header,
            line,
            ProductionTransferResidualDraftSupport.ResolvePrimaryPickTask(header));

        Assert.Equal(sourceC, resolved);
    }

    [Fact]
    public void ResolveResidualSourceLocationId_prefers_single_tracking_source()
    {
        const long wtLineId = 11;
        const long sourceB = 202;
        var line = new WarehouseTransferLine
        {
            Id = wtLineId,
            DefaultSourceLocationId = null,
            Trackings =
            [
                new WarehouseTransferTracking
                {
                    PlannedQuantity = 2,
                    PickedQuantity = 1,
                    SourceLocationId = sourceB,
                },
            ],
        };

        var resolved = ProductionTransferResidualDraftSupport.ResolveResidualSourceLocationId(
            new WarehouseTransferHeader(),
            line,
            null);

        Assert.Equal(sourceB, resolved);
    }

    [Fact]
    public void NeedsAutoAssignSources_is_true_when_same_warehouse_line_missing_source()
    {
        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = 1,
            TargetWarehouseId = 1,
        };
        var draftLines = new[]
        {
            new verii_wms_api_v2.Modules.WarehouseTransfer.Application.WarehouseTransferLineDraftRequest(
                13, null, 5, "ADET", StockTrackingType.None, false, null, 303, null, null, null, null, null),
        };

        Assert.True(ProductionTransferResidualDraftSupport.NeedsAutoAssignSources(header, draftLines));
    }

    [Fact]
    public void NeedsAutoAssignSources_is_true_when_different_warehouse_line_missing_source()
    {
        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = 1,
            TargetWarehouseId = 2,
        };
        var draftLines = new[]
        {
            new verii_wms_api_v2.Modules.WarehouseTransfer.Application.WarehouseTransferLineDraftRequest(
                13, null, 4, "ADET", StockTrackingType.None, false, null, 303, null, null, null, null, null),
        };

        Assert.True(ProductionTransferResidualDraftSupport.NeedsAutoAssignSources(header, draftLines));
    }

    [Fact]
    public void NeedsAutoAssignSources_is_false_when_different_warehouse_lines_have_source()
    {
        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = 1,
            TargetWarehouseId = 2,
        };
        var draftLines = new[]
        {
            new verii_wms_api_v2.Modules.WarehouseTransfer.Application.WarehouseTransferLineDraftRequest(
                13, null, 4, "ADET", StockTrackingType.None, false, 101, 303, null, null, null, null, null),
        };

        Assert.False(ProductionTransferResidualDraftSupport.NeedsAutoAssignSources(header, draftLines));
    }

    [Fact]
    public void NeedsAutoAssignSources_is_false_when_same_warehouse_lines_have_locations()
    {
        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = 1,
            TargetWarehouseId = 1,
        };
        var draftLines = new[]
        {
            new verii_wms_api_v2.Modules.WarehouseTransfer.Application.WarehouseTransferLineDraftRequest(
                13, null, 5, "ADET", StockTrackingType.None, false, 101, 303, null, null, null, null, null),
        };

        Assert.False(ProductionTransferResidualDraftSupport.NeedsAutoAssignSources(header, draftLines));
    }

    [Fact]
    public void BuildLineDraft_defers_tracking_capture_when_serial_required_and_unpicked_is_mixed()
    {
        var header = new WarehouseTransferHeader
        {
            Id = 1,
            DocumentNo = "PT-164",
            SourceWarehouseId = 1,
            TargetWarehouseId = 1,
        };
        var line = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 13,
            UnitCode = "ADET",
            TrackingType = StockTrackingType.Serial,
            RequireSerial = true,
            DefaultSourceLocationId = 101,
            DefaultTargetLocationId = 303,
            Trackings =
            [
                new WarehouseTransferTracking
                {
                    PlannedQuantity = 1,
                    PickedQuantity = 0,
                    SerialNo = "VCX-2",
                    SourceLocationId = 101,
                },
                new WarehouseTransferTracking
                {
                    PlannedQuantity = 18,
                    PickedQuantity = 0,
                    SerialNo = null,
                    SourceLocationId = 101,
                },
            ],
        };

        var draft = ProductionTransferResidualDraftSupport.BuildLineDraft(header, line, null, 19);

        Assert.Null(draft.Trackings);
        Assert.True(ProductionTransferResidualDraftSupport.RequiresDeferredTrackingCapture(draft));
        Assert.True(ProductionTransferResidualDraftSupport.NeedsAutoAssignSources(header, [draft]));
    }

    [Fact]
    public void BuildLineDraft_keeps_complete_serial_trackings_when_all_unpicked_have_serial()
    {
        var header = new WarehouseTransferHeader
        {
            Id = 1,
            DocumentNo = "PT-200",
            SourceWarehouseId = 1,
            TargetWarehouseId = 1,
        };
        var line = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 13,
            UnitCode = "ADET",
            TrackingType = StockTrackingType.Serial,
            RequireSerial = true,
            DefaultSourceLocationId = 101,
            DefaultTargetLocationId = 303,
            Trackings =
            [
                new WarehouseTransferTracking
                {
                    PlannedQuantity = 1,
                    PickedQuantity = 0,
                    SerialNo = "VCX-2",
                    SourceLocationId = 101,
                },
                new WarehouseTransferTracking
                {
                    PlannedQuantity = 1,
                    PickedQuantity = 0,
                    SerialNo = "VCX-3",
                    SourceLocationId = 101,
                },
            ],
        };

        var draft = ProductionTransferResidualDraftSupport.BuildLineDraft(header, line, null, 2);

        Assert.NotNull(draft.Trackings);
        Assert.Equal(2, draft.Trackings!.Count);
        Assert.False(ProductionTransferResidualDraftSupport.RequiresDeferredTrackingCapture(draft));
    }

    [Fact]
    public void Residual_draft_with_missing_source_passes_policy_when_auto_assign_enabled()
    {
        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = 1,
            TargetWarehouseId = 2,
            RequireSourceLocation = true,
            RequireTargetLocation = false,
        };
        var draftLines = new[]
        {
            new WarehouseTransferLineDraftRequest(
                13, null, 4, "ADET", StockTrackingType.None, false, null, 303, null, null, null, null, null),
        };

        Assert.True(ProductionTransferResidualDraftSupport.NeedsAutoAssignSources(header, draftLines));

        var request = new CreateWarehouseTransferDraftRequest(
            Guid.NewGuid(),
            "0",
            1,
            DateOnly.FromDateTime(DateTime.UtcNow),
            WarehouseTransferInitiationMode.StockBasedTask,
            WarehouseTransferProcessType.InternalRequest,
            1,
            2,
            null,
            null,
            null,
            null,
            null,
            3,
            "KALAN:PT-1",
            "eksik teslim kalan",
            draftLines,
            null,
            WarehouseTransferBusinessContext.ProductionMaterialSupply,
            null,
            true);

        var exception = Record.Exception(() =>
            WarehouseTransferDraftPolicyGuard.Validate(
                request,
                ProductionTransferWarehousePolicyAdapter.FromProductionSnapshot(header)));

        Assert.Null(exception);
    }

    [Fact]
    public void BuildConsolidatedResidualGroups_merges_same_stock_split_across_shelves()
    {
        var header = new WarehouseTransferHeader
        {
            Id = 1,
            DocumentNo = "PT-258",
            SourceWarehouseId = 1,
            TargetWarehouseId = 2,
            TargetPutawayLocationId = 303,
            Lines =
            [
                NonSerialLine(10, 1, 13, source: 3, requested: 5, picked: 2),
                NonSerialLine(11, 2, 13, source: 2, requested: 3, picked: 0),
                NonSerialLine(12, 3, 13, source: 1, requested: 1, picked: 0),
                NonSerialLine(13, 4, 13, source: null, requested: 1, picked: 0),
            ],
        };
        var link = SameRequirementLink(100, 10, 11, 12, 13);

        var groups = ProductionTransferResidualDraftSupport.BuildConsolidatedResidualGroups(header, link, null);

        var group = Assert.Single(groups);
        Assert.Equal(8, group.RemainingQuantity);
        Assert.Equal(8, group.Draft.Quantity);
        Assert.Equal(13, group.Draft.StockId);
        Assert.Null(group.Draft.DefaultSourceLocationId);
        Assert.Equal(100, group.SourceLink.ProductionConsumptionId);
    }

    [Fact]
    public void BuildConsolidatedResidualGroups_keeps_single_source_when_all_remainders_share_shelf()
    {
        const long a1 = 1;
        var header = new WarehouseTransferHeader
        {
            Id = 1,
            DocumentNo = "PT-1",
            SourceWarehouseId = 1,
            TargetWarehouseId = 2,
            TargetPutawayLocationId = 303,
            Lines =
            [
                NonSerialLine(10, 1, 13, source: a1, requested: 2, picked: 0),
                NonSerialLine(11, 2, 13, source: a1, requested: 5, picked: 0),
            ],
        };
        var link = SameRequirementLink(100, 10, 11);

        var groups = ProductionTransferResidualDraftSupport.BuildConsolidatedResidualGroups(header, link, null);

        var group = Assert.Single(groups);
        Assert.Equal(7, group.RemainingQuantity);
        Assert.Equal(a1, group.Draft.DefaultSourceLocationId);
    }

    [Fact]
    public void BuildConsolidatedResidualGroups_does_not_merge_different_consumption_or_serial_lines()
    {
        var header = new WarehouseTransferHeader
        {
            Id = 1,
            DocumentNo = "PT-1",
            SourceWarehouseId = 1,
            TargetWarehouseId = 2,
            TargetPutawayLocationId = 303,
            Lines =
            [
                NonSerialLine(10, 1, 13, source: 1, requested: 3, picked: 0),
                NonSerialLine(11, 2, 13, source: 2, requested: 4, picked: 0),
                new WarehouseTransferLine
                {
                    Id = 12,
                    LineNo = 3,
                    StockId = 13,
                    UnitCode = "ADET",
                    TrackingType = StockTrackingType.Serial,
                    RequireSerial = true,
                    DefaultSourceLocationId = 1,
                    RequestedQuantity = 1,
                    PickedQuantity = 0,
                    Trackings =
                    [
                        new WarehouseTransferTracking { PlannedQuantity = 1, PickedQuantity = 0, SerialNo = "SN-1" },
                    ],
                },
            ],
        };
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new() { WarehouseTransferLineId = 10, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 3 },
                new() { WarehouseTransferLineId = 11, ProductionConsumptionId = 200, RequirementReference = "REQ-1", RequiredQuantity = 4 },
                new() { WarehouseTransferLineId = 12, ProductionConsumptionId = 100, RequirementReference = "REQ-1", RequiredQuantity = 1 },
            ],
        };

        var groups = ProductionTransferResidualDraftSupport.BuildConsolidatedResidualGroups(header, link, null);

        Assert.Equal(3, groups.Count);
        Assert.Equal(new decimal[] { 3, 4, 1 }, groups.Select(x => x.RemainingQuantity));
    }

    private static WarehouseTransferLine NonSerialLine(
        long id,
        int lineNo,
        long stockId,
        long? source,
        decimal requested,
        decimal picked) =>
        new()
        {
            Id = id,
            LineNo = lineNo,
            StockId = stockId,
            UnitCode = "ADET",
            TrackingType = StockTrackingType.None,
            DefaultSourceLocationId = source,
            DefaultTargetLocationId = 303,
            RequestedQuantity = requested,
            PickedQuantity = picked,
            Trackings = [],
        };

    private static ProductionTransferHeaderLink SameRequirementLink(long consumptionId, params long[] lineIds) =>
        new()
        {
            Lines = lineIds.Select(lineId => new ProductionTransferLineLink
            {
                WarehouseTransferLineId = lineId,
                ProductionConsumptionId = consumptionId,
                RequirementReference = "REQ-1",
                LineRole = ProductionTransferLineRole.ConsumptionSupply,
                RequiredQuantity = 1,
            }).ToList(),
        };
}
