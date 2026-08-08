using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public interface IProductionTransferErpPostingCoordinator
{
    Task<ErpPostingResult?> PostIfEligibleAsync(
        long transferId,
        long actorUserId,
        CancellationToken cancellationToken = default);

    Task<ErpPostingResult?> PostNowAsync(
        long transferId,
        Guid idempotencyKey,
        long actorUserId,
        CancellationToken cancellationToken = default);
}

public static class ProductionTransferErpPostingPolicyEvaluator
{
    public static bool IsEligible(
        ProductionTransferErpPostingPolicy policy,
        ProductionTransferWorkflowStatus workflowStatus,
        WarehouseTransferStatus transferStatus,
        ErpIntegrationStatus erpStatus) =>
        policy == ProductionTransferErpPostingPolicy.AfterHandover
        && workflowStatus is ProductionTransferWorkflowStatus.Completed
            or ProductionTransferWorkflowStatus.CompletedWithShortage
        && transferStatus is WarehouseTransferStatus.Completed
            or WarehouseTransferStatus.CompletedWithShortage
        && erpStatus is ErpIntegrationStatus.Pending or ErpIntegrationStatus.Failed;
}
