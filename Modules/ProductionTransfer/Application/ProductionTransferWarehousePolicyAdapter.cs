using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

/// <summary>
/// Isolates production-transfer workflow settings from ordinary inter-warehouse transfer settings.
/// The shared transfer engine still owns all physical stock, serial, lot, location and balance checks.
/// </summary>
internal static class ProductionTransferWarehousePolicyAdapter
{
    public static WarehouseTransferDraftPolicyContext FromProductionPolicy(ProductionTransferPolicy policy) => new(
        ValidateInitiationMode: false,
        AllowOrderBasedTask: true,
        AllowStockBasedTask: true,
        AllowOrderBasedDirect: true,
        AllowStockBasedDirect: true,
        RequireApproval: policy.RequireApproval,
        // RequireTaskAssignment controls task-based execution, not whether an assignee must exist at draft time.
        RequireAssigneeForTask: false,
        AllowMultipleAssignees: false,
        AutoReleaseTaskBased: false,
        ReservationPolicy: WarehouseTransferReservationPolicy.OnRelease,
        MinimumFulfillmentPercent: policy.AllowPartialSupply ? 0m : 100m,
        AllowPartialPicking: policy.AllowPartialSupply,
        AllowPartialShipment: policy.AllowPartialSupply,
        AllowPartialReceipt: policy.AllowPartialSupply,
        RequireDestinationAcceptance: true,
        CreateTransitInventory: false,
        RequirePutaway: false,
        RequireSourceLocation: policy.RequireSourceProductionLocation,
        RequireTargetLocation: policy.RequireTargetProductionLocation,
        RequireShipmentInformation: false,
        DirectPostingPolicy: WarehouseTransferDirectPostingPolicy.OneStep,
        DiscrepancyPolicy: policy.AllowPartialSupply
            ? WarehouseTransferDiscrepancyPolicy.AllowWithReason
            : WarehouseTransferDiscrepancyPolicy.Block,
        CancellationReturnPolicy: policy.CancellationReturnPolicy);

    public static WarehouseTransferDraftPolicyContext FromProductionSnapshot(WarehouseTransferHeader header) => new(
        ValidateInitiationMode: false,
        AllowOrderBasedTask: true,
        AllowStockBasedTask: true,
        AllowOrderBasedDirect: true,
        AllowStockBasedDirect: true,
        RequireApproval: header.RequireApproval,
        RequireAssigneeForTask: false,
        AllowMultipleAssignees: false,
        AutoReleaseTaskBased: header.AutoRelease,
        ReservationPolicy: header.ReservationPolicy,
        MinimumFulfillmentPercent: header.MinimumFulfillmentPercent,
        AllowPartialPicking: header.AllowPartialPicking,
        AllowPartialShipment: header.AllowPartialShipment,
        AllowPartialReceipt: header.AllowPartialReceipt,
        RequireDestinationAcceptance: header.RequireDestinationAcceptance,
        CreateTransitInventory: header.CreateTransitInventory,
        RequirePutaway: header.RequirePutaway,
        RequireSourceLocation: header.RequireSourceLocation,
        RequireTargetLocation: header.RequireTargetLocation,
        RequireShipmentInformation: header.RequireShipmentInformation,
        DirectPostingPolicy: header.DirectPostingPolicy,
        DiscrepancyPolicy: header.DiscrepancyPolicy,
        CancellationReturnPolicy: header.CancellationReturnPolicy);
}
