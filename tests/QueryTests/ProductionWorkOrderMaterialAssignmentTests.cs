using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionWorkOrderMaterialAssignmentTests
{
    [Fact]
    public void SplitByAssignedCoverage_returns_remaining_and_assigned_materials()
    {
        var materials = new[]
        {
            CreateMaterial(1, 10, 100, 5),
            CreateMaterial(2, 20, 100, 10),
        };
        var assigned = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 5,
            [ProductionWorkOrderMaterialAssignment.CreateKey(2, 20, 100)] = 4,
        };

        var split = ProductionWorkOrderMaterialAssignment.SplitByAssignedCoverage(materials, assigned);

        Assert.Single(split.Remaining);
        Assert.Equal(6, split.Remaining[0].RequiredQuantity);
        Assert.Equal(2, split.Assigned.Count);
        Assert.Contains(split.Assigned, x => x.StockId == 1 && x.RequiredQuantity == 5);
        Assert.Contains(split.Assigned, x => x.StockId == 2 && x.RequiredQuantity == 4);
    }

    [Fact]
    public void ApplyAssignedCoverage_keeps_unassigned_materials()
    {
        var materials = new[]
        {
            CreateMaterial(1, 10, 100, 5),
            CreateMaterial(2, 20, 100, 7),
        };

        var remaining = ProductionWorkOrderMaterialAssignment.ApplyAssignedCoverage(materials, new Dictionary<ProductionRecipeMaterialKey, decimal>());

        Assert.Equal(2, remaining.Count);
        Assert.Equal(5, remaining[0].RequiredQuantity);
        Assert.Equal(7, remaining[1].RequiredQuantity);
    }

    [Fact]
    public void ApplyAssignedCoverage_removes_fully_assigned_material_and_scales_partial()
    {
        var materials = new[]
        {
            CreateMaterial(1, 10, 100, 5),
            CreateMaterial(2, 20, 100, 10),
        };
        var assigned = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 5,
            [ProductionWorkOrderMaterialAssignment.CreateKey(2, 20, 100)] = 4,
        };

        var remaining = ProductionWorkOrderMaterialAssignment.ApplyAssignedCoverage(materials, assigned);

        Assert.Single(remaining);
        Assert.Equal(2, remaining[0].StockId);
        Assert.Equal(6, remaining[0].RequiredQuantity);
        Assert.Equal(6, remaining[0].RecipeQuantity);
    }

    [Fact]
    public void IsFullyAssigned_is_true_when_all_recipe_lines_are_covered()
    {
        var materials = new[] { CreateMaterial(1, 10, 100, 5) };
        var assigned = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 5,
        };

        Assert.True(ProductionWorkOrderMaterialAssignment.IsFullyAssigned(materials, assigned));
    }

    [Fact]
    public void IsFullyAssigned_is_false_when_partial_transfer_remainders_exist()
    {
        var materials = new[] { CreateMaterial(1, 10, 100, 5) };
        var assigned = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 5,
        };
        var remainders = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 2,
        };

        Assert.False(ProductionWorkOrderMaterialAssignment.IsFullyAssigned(materials, assigned, remainders));
    }

    [Fact]
    public void ResolveCommittedAssignedQuantity_uses_handed_over_after_shortage_handover()
    {
        var quantity = ProductionWorkOrderMaterialAssignment.ResolveCommittedAssignedQuantity(
            ProductionTransferWorkflowStatus.CompletedWithShortage,
            requiredQuantity: 10,
            handedOverQuantity: 4,
            requestedQuantity: 10);

        Assert.Equal(4, quantity);
    }

    [Fact]
    public void ResolveCommittedAssignedQuantity_uses_required_quantity_for_open_transfer()
    {
        var quantity = ProductionWorkOrderMaterialAssignment.ResolveCommittedAssignedQuantity(
            ProductionTransferWorkflowStatus.Picking,
            requiredQuantity: 10,
            handedOverQuantity: 0,
            requestedQuantity: 8);

        Assert.Equal(10, quantity);
    }

    [Fact]
    public void ReclassifyPartialTransferRemainders_does_not_inflate_remaining_after_shortage_handover()
    {
        var materials = new[] { CreateMaterial(1, 10, 100, 3) };
        var assigned = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 1,
        };
        var split = ProductionWorkOrderMaterialAssignment.SplitByAssignedCoverage(materials, assigned);
        var remainders = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 2,
        };

        var reclassified = ProductionWorkOrderMaterialAssignment.ReclassifyPartialTransferRemainders(
            materials,
            split.Remaining,
            split.Assigned,
            remainders);

        Assert.Single(reclassified.Remaining);
        Assert.Equal(2, reclassified.Remaining[0].RequiredQuantity);
        Assert.Single(reclassified.Assigned);
        Assert.Equal(1, reclassified.Assigned[0].RequiredQuantity);
    }

    [Fact]
    public void ReclassifyPartialTransferRemainders_moves_quantities_from_assigned_to_remaining()
    {
        var materials = new[]
        {
            CreateMaterial(1, 10, 100, 5),
            CreateMaterial(2, 20, 100, 10),
        };
        var assigned = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 5,
            [ProductionWorkOrderMaterialAssignment.CreateKey(2, 20, 100)] = 10,
        };
        var split = ProductionWorkOrderMaterialAssignment.SplitByAssignedCoverage(materials, assigned);
        var remainders = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(2, 20, 100)] = 4,
        };

        var reclassified = ProductionWorkOrderMaterialAssignment.ReclassifyPartialTransferRemainders(
            materials,
            split.Remaining,
            split.Assigned,
            remainders);

        Assert.Single(reclassified.Remaining);
        Assert.Equal(4, reclassified.Remaining[0].RequiredQuantity);
        Assert.Single(reclassified.Assigned);
        Assert.Equal(1, reclassified.Assigned[0].StockId);
        Assert.Equal(5, reclassified.Assigned[0].RequiredQuantity);
    }

    [Fact]
    public void ResolveEffectivePickedQuantity_sums_tracking_picks_for_serial_lines()
    {
        var line = new WarehouseTransferLine
        {
            RequestedQuantity = 2,
            PickedQuantity = 0,
            Trackings =
            [
                new WarehouseTransferTracking { PlannedQuantity = 1, PickedQuantity = 1 },
                new WarehouseTransferTracking { PlannedQuantity = 1, PickedQuantity = 0 },
            ],
        };

        var quantity = ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line);

        Assert.Equal(1, quantity);
    }

    [Fact]
    public void ResolveCommittedAssignedQuantity_uses_effective_picked_when_handed_over_missing()
    {
        var line = new WarehouseTransferLine
        {
            RequestedQuantity = 2,
            PickedQuantity = 0,
            Trackings =
            [
                new WarehouseTransferTracking { PlannedQuantity = 1, PickedQuantity = 1 },
                new WarehouseTransferTracking { PlannedQuantity = 1, PickedQuantity = 0 },
            ],
        };

        var quantity = ProductionWorkOrderMaterialAssignment.ResolveCommittedAssignedQuantity(
            ProductionTransferWorkflowStatus.CompletedWithShortage,
            requiredQuantity: 2,
            handedOverQuantity: 0,
            transferLine: line);

        Assert.Equal(1, quantity);
    }

    [Fact]
    public void NetPartialTransferRemaindersAgainstOpenAssignments_subtracts_open_manual_assignments()
    {
        var key = ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100);
        var remainders = new Dictionary<ProductionRecipeMaterialKey, decimal> { [key] = 1 };
        var openAssignments = new Dictionary<ProductionRecipeMaterialKey, decimal> { [key] = 2 };

        ProductionWorkOrderMaterialAssignment.NetPartialTransferRemaindersAgainstOpenAssignments(
            remainders,
            openAssignments);

        Assert.Empty(remainders);
    }

    [Fact]
    public void BuildRequirementReference_includes_operation_number()
    {
        Assert.Equal(
            "IE-100#25",
            ProductionWorkOrderMaterialAssignment.BuildRequirementReference("IE-100", 25));
        Assert.True(ProductionWorkOrderMaterialAssignment.TryParseOperationNumber("IE-100#25", out var operation));
        Assert.Equal(25, operation);
    }

    [Fact]
    public void SubtractCancelledQuantities_reduces_remaining_and_drops_fully_cancelled_lines()
    {
        var materials = new[]
        {
            CreateMaterial(1, 10, 100, 5),
            CreateMaterial(2, 20, 100, 10),
        };
        var cancelled = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 5,
            [ProductionWorkOrderMaterialAssignment.CreateKey(2, 20, 100)] = 4,
        };

        var remaining = ProductionWorkOrderMaterialAssignment.SubtractCancelledQuantities(materials, cancelled);

        Assert.Single(remaining);
        Assert.Equal(2, remaining[0].StockId);
        Assert.Equal(6, remaining[0].RequiredQuantity);
    }

    private static PreparedNetsisProductionMaterial CreateMaterial(long stockId, long? yapCodeId, int operationNumber, decimal required) =>
        new(stockId, $"STK-{stockId}", "Stok", "ADET", yapCodeId, null, operationNumber, required, 0, required, null);

    [Fact]
    public void BuildKalanOpenMaterials_uses_open_task_line_quantity()
    {
        var transferLine = new WarehouseTransferLine
        {
            Id = 10,
            StockId = 1,
            YapCodeId = 10,
            UnitCode = "ADET",
        };
        var link = new ProductionTransferHeaderLink
        {
            Lines =
            [
                new ProductionTransferLineLink
                {
                    IsDeleted = false,
                    WarehouseTransferLineId = 10,
                    RequirementReference = "IE-100#100",
                },
            ],
        };
        var kalanTask = new WarehouseTransferTask
        {
            Lines =
            [
                new WarehouseTransferTaskLine
                {
                    IsDeleted = false,
                    Line = transferLine,
                    PlannedQuantity = 5,
                    ProcessedQuantity = 2,
                },
            ],
        };

        var materials = ProductionWorkOrderMaterialAssignment.BuildKalanOpenMaterials(link, kalanTask);

        Assert.Single(materials);
        Assert.Equal(3, materials[0].RequiredQuantity);
        Assert.Equal(100, materials[0].OperationNumber);
    }

    [Fact]
    public void IsFullyAssigned_requires_all_kalan_materials_not_source_transfer_only()
    {
        var kalanMaterials = new List<PreparedNetsisProductionMaterial>
        {
            CreateMaterial(1, 10, 100, 3),
            CreateMaterial(2, 20, 100, 4),
        };

        var sourceOnlyAssigned = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 3,
        };
        Assert.False(ProductionWorkOrderMaterialAssignment.IsFullyAssigned(kalanMaterials, sourceOnlyAssigned));

        var fullyAssigned = new Dictionary<ProductionRecipeMaterialKey, decimal>
        {
            [ProductionWorkOrderMaterialAssignment.CreateKey(1, 10, 100)] = 3,
            [ProductionWorkOrderMaterialAssignment.CreateKey(2, 20, 100)] = 4,
        };
        Assert.True(ProductionWorkOrderMaterialAssignment.IsFullyAssigned(kalanMaterials, fullyAssigned));
    }
}
