using verii_wms_api_v2.Modules.ProductionTransfer.Application;
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
}
