using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferPolicyIsolationTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, false)]
    public void ProductionLocationRequirements_DoNotInheritWarehouseTransferPolicy(
        bool productionRequiresSource,
        bool productionRequiresTarget,
        bool warehouseRequiresSource,
        bool warehouseRequiresTarget)
    {
        var productionPolicy = new ProductionTransferPolicy
        {
            RequireSourceProductionLocation = productionRequiresSource,
            RequireTargetProductionLocation = productionRequiresTarget,
        };
        var warehousePolicy = WarehousePolicy(
            requireSource: warehouseRequiresSource,
            requireTarget: warehouseRequiresTarget);

        var productionContext = ProductionTransferWarehousePolicyAdapter.FromProductionPolicy(productionPolicy);
        var warehouseContext = WarehouseTransferDraftPolicyContext.FromWarehousePolicy(warehousePolicy);

        Assert.Equal(productionRequiresSource, productionContext.RequireSourceLocation);
        Assert.Equal(productionRequiresTarget, productionContext.RequireTargetLocation);
        Assert.Equal(warehouseRequiresSource, warehouseContext.RequireSourceLocation);
        Assert.Equal(warehouseRequiresTarget, warehouseContext.RequireTargetLocation);
        Assert.False(productionContext.ValidateInitiationMode);
        Assert.True(warehouseContext.ValidateInitiationMode);
    }

    [Fact]
    public void ProductionWorkflowSnapshot_IsOwnedByProductionPolicy()
    {
        var productionContext = ProductionTransferWarehousePolicyAdapter.FromProductionPolicy(new()
        {
            RequireApproval = true,
            AllowPartialSupply = false,
            CancellationReturnPolicy = WarehouseTransferCancellationReturnPolicy.ManagerSelectionRequired,
        });

        Assert.True(productionContext.RequireApproval);
        Assert.False(productionContext.AllowPartialPicking);
        Assert.Equal(100m, productionContext.MinimumFulfillmentPercent);
        Assert.False(productionContext.CreateTransitInventory);
        Assert.Equal(WarehouseTransferDirectPostingPolicy.OneStep, productionContext.DirectPostingPolicy);
        Assert.Equal(
            WarehouseTransferCancellationReturnPolicy.ManagerSelectionRequired,
            productionContext.CancellationReturnPolicy);
    }

    [Fact]
    public void OppositeTargetRequirements_AreEnforcedOnlyByTheirOwningModule()
    {
        var request = DraftWithoutLocations(autoAssignSources: true);
        var productionContext = ProductionTransferWarehousePolicyAdapter.FromProductionPolicy(new()
        {
            RequireSourceProductionLocation = true,
            RequireTargetProductionLocation = false,
        });
        var warehouseContext = WarehouseTransferDraftPolicyContext.FromWarehousePolicy(
            WarehousePolicy(requireSource: false, requireTarget: true));

        WarehouseTransferDraftPolicyGuard.Validate(request, productionContext);
        var error = Assert.Throws<AppException>(() => WarehouseTransferDraftPolicyGuard.Validate(request, warehouseContext));

        Assert.Contains("hedef raf", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CreateWarehouseTransferDraftRequest DraftWithoutLocations(bool autoAssignSources) => new(
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
        null,
        null,
        [new WarehouseTransferLineDraftRequest(1, null, 1m, "ADET", StockTrackingType.None, false, null, null, null, [], null)],
        [],
        WarehouseTransferBusinessContext.ProductionMaterialSupply,
        null,
        autoAssignSources);

    private static WarehouseTransferPolicyDto WarehousePolicy(bool requireSource, bool requireTarget) => new(
        1,
        "0",
        true,
        true,
        false,
        true,
        false,
        true,
        true,
        false,
        WarehouseTransferReservationPolicy.OnRelease,
        100m,
        true,
        true,
        true,
        true,
        true,
        true,
        requireSource,
        requireTarget,
        false,
        WarehouseTransferDirectPostingPolicy.TwoStepTransit,
        WarehouseTransferDiscrepancyPolicy.RequireApproval,
        WarehouseTransferCancellationReturnPolicy.OriginalSourceLocation,
        null,
        null);
}
