using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Production.Application;

public sealed record ProductionMaterialDraftRequest(
    long StockId,
    long? YapCodeId,
    decimal RequiredQuantity,
    long SourceWarehouseId,
    long? PreferredSourceLocationId,
    ProductionMaterialIssueMode IssueMode,
    bool IsMandatory);

public sealed record ProductionOutputDraftRequest(
    long StockId,
    long? YapCodeId,
    decimal PlannedQuantity,
    long TargetWarehouseId,
    long? PreferredTargetLocationId,
    bool IsPrimary);

public sealed record ProductionOrderDraftRequest(
    string LocalKey,
    string? ExternalOrderNo,
    string? ExternalSourceSystemCode,
    int SequenceNo,
    int? ParallelGroupNo,
    string? BomReference,
    string? RoutingReference,
    string? WorkCenterCode,
    long ProducedStockId,
    long? ProducedYapCodeId,
    decimal PlannedQuantity,
    long SourceWarehouseId,
    long TargetWarehouseId,
    bool RequireMaterialTransferBeforeStart,
    DateTimeOffset? PlannedStartAtUtc,
    DateTimeOffset? PlannedEndAtUtc,
    string? Description,
    IReadOnlyList<long>? AssignedUserIds,
    IReadOnlyList<ProductionMaterialDraftRequest>? Materials,
    IReadOnlyList<ProductionOutputDraftRequest>? Outputs);

public sealed record ProductionDependencyDraftRequest(
    string PredecessorOrderLocalKey,
    string SuccessorOrderLocalKey,
    ProductionDependencyType DependencyType,
    int LagMinutes,
    bool RequireOutputAvailable,
    bool RequireTransferCompleted);

public sealed record CreateProductionPlanRequest(
    Guid IdempotencyKey,
    string BranchCode,
    long DocumentSeriesId,
    DateOnly DocumentDate,
    ProductionPlanType PlanType,
    ProductionExecutionMode ExecutionMode,
    byte Priority,
    long? CustomerId,
    DateTimeOffset? PlannedStartAtUtc,
    DateTimeOffset? PlannedEndAtUtc,
    string? Description,
    IReadOnlyList<ProductionOrderDraftRequest> Orders,
    IReadOnlyList<ProductionDependencyDraftRequest>? Dependencies);

public sealed record CreateProductionPlanResult(
    long Id,
    string DocumentNo,
    int OrderCount,
    int MaterialCount,
    int OutputCount,
    bool Replayed);

public sealed record PreparedNetsisProductionMaterial(
    long? StockId,
    string StockCode,
    string? StockName,
    string UnitCode,
    long? YapCodeId,
    string? ConfigurationCode,
    int OperationNumber,
    decimal RecipeQuantity,
    decimal WasteQuantity,
    decimal RequiredQuantity,
    string? MappingError);

public sealed record PreparedNetsisProductionWorkOrder(
    ProductionOrderSourceType SourceType,
    string SourceSystemCode,
    string WorkOrderNumber,
    int BranchCode,
    string ProductCode,
    string ProductName,
    string UnitCode,
    decimal PlannedQuantity,
    long? ProducedStockId,
    long? ProducedYapCodeId,
    string? ConfigurationCode,
    long? SourceWarehouseId,
    int SourceWarehouseCode,
    string? SourceWarehouseName,
    long? TargetWarehouseId,
    int TargetWarehouseCode,
    string? TargetWarehouseName,
    DateTime? WorkOrderDate,
    DateTime? DeliveryDate,
    string? ProjectCode,
    bool IsClosed,
    long? ExistingProductionHeaderId,
    long? ExistingProductionOrderId,
    string? ExistingProductionDocumentNo,
    IReadOnlyList<string> MappingErrors,
    IReadOnlyList<PreparedNetsisProductionMaterial> Materials,
    IReadOnlyList<PreparedNetsisProductionMaterial> AssignedMaterials,
    ProductionSourceWorkOrderListingKind ListingKind = ProductionSourceWorkOrderListingKind.Standard,
    long? TransferId = null,
    long? KalanTaskId = null);

public enum ProductionSourceWorkOrderListingKind
{
    Standard = 0,
    CancellationReturnRemainder = 1,
    ManagerCancelledAssignment = 2,
    RestoredCancelledAssignment = 3
}

public sealed record ProductionSourceWorkOrderRow(
    ProductionOrderSourceType SourceType,
    string SourceSystemCode,
    int RevisionNumber,
    string WorkOrderNumber,
    int BranchCode,
    string StockCode,
    string StockName,
    string? ConfigurationCode,
    decimal WorkOrderQuantity,
    string? UnitCode,
    decimal RecipeTotal,
    DateTime? WorkOrderDate,
    DateTime? DeliveryDate,
    string? ProjectCode,
    int WarehouseCode,
    int IssueWarehouseCode,
    bool IsClosed,
    ProductionSourceWorkOrderListingKind ListingKind = ProductionSourceWorkOrderListingKind.Standard,
    long? TransferId = null,
    long? KalanTaskId = null,
    long? CancellationId = null,
    int AssignedRecipeLineCount = 0,
    int RecipeLineCount = 0);

public sealed record CancelProductionWorkOrderAssignmentRequest(
    Guid IdempotencyKey,
    string WorkOrderNumber,
    ProductionOrderSourceType? SourceType,
    string? SourceSystemCode,
    string Reason,
    long? TransferId,
    long? KalanTaskId,
    IReadOnlyList<CancelProductionWorkOrderAssignmentLineRequest>? Lines);

public sealed record CancelProductionWorkOrderAssignmentLineRequest(
    long? StockId,
    long? YapCodeId,
    int OperationNumber,
    decimal Quantity);

public sealed record RestoreProductionWorkOrderAssignmentRequest(
    Guid IdempotencyKey,
    string WorkOrderNumber,
    string? Reason,
    IReadOnlyList<CancelProductionWorkOrderAssignmentLineRequest>? Lines);

public sealed record ProductionWorkOrderAssignmentCancellationResult(
    long CancellationId,
    string WorkOrderNumber,
    ProductionWorkOrderAssignmentCancellationStatus Status,
    decimal CancelledQuantityTotal,
    bool Replayed);

public enum ProductionReturnedWorkOrderKind
{
    AssignmentReturnRemainder = 1,
    CancellationReturnRemainder = 2,
    PartialTransferRemainder = 3
}

public sealed record ProductionReturnedWorkOrderRow(
    string WorkOrderNumber,
    long TransferId,
    string DocumentNo,
    long KalanTaskId,
    string KalanTaskNo,
    string KalanTaskDisplayLabel,
    decimal RemainingQuantity,
    decimal PlannedQuantity,
    DateOnly? DocumentDate,
    string? ProjectCode,
    int SourceWarehouseCode,
    int TargetWarehouseCode,
    long SourceWarehouseId,
    long TaskWarehouseId,
    ProductionReturnedWorkOrderKind ReturnKind);

public sealed record ProductionPlanGridRow(
    long Id,
    string BranchCode,
    string DocumentNo,
    DateOnly DocumentDate,
    ProductionPlanType PlanType,
    ProductionExecutionMode ExecutionMode,
    ProductionPlanStatus Status,
    byte Priority,
    string? CustomerCode,
    string? CustomerName,
    int OrderCount,
    int MaterialCount,
    int OutputCount,
    decimal PlannedQuantity,
    decimal CompletedQuantity,
    DateTimeOffset? PlannedStartAtUtc,
    DateTimeOffset? PlannedEndAtUtc,
    long? CreatedBy,
    DateTime? CreatedDate,
    long? UpdatedBy,
    DateTime? UpdatedDate);

public sealed record ProductionMaterialDto(
    long Id,
    int LineNo,
    long StockId,
    string StockCode,
    string? StockName,
    long? YapCodeId,
    string? YapCode,
    string UnitCode,
    decimal RequiredQuantity,
    decimal IssuedQuantity,
    decimal ConsumedQuantity,
    ProductionMaterialIssueMode IssueMode,
    bool IsMandatory,
    long SourceWarehouseId,
    long? PreferredSourceLocationId,
    StockTrackingType TrackingType);

public sealed record ProductionOutputDto(
    long Id,
    int LineNo,
    long StockId,
    string StockCode,
    string? StockName,
    long? YapCodeId,
    string? YapCode,
    string UnitCode,
    decimal PlannedQuantity,
    decimal ProducedQuantity,
    decimal ScrapQuantity,
    long TargetWarehouseId,
    long? PreferredTargetLocationId,
    StockTrackingType TrackingType,
    bool IsPrimary);

public sealed record ProductionAssignmentDto(
    long Id,
    long UserId,
    string Username,
    string DisplayName,
    bool IsPrimary,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? AcceptedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Note);

public sealed record ProductionOrderDto(
    long Id,
    int LineNo,
    string OrderNo,
    string? ExternalOrderNo,
    string? ExternalSourceSystemCode,
    ProductionOrderStatus Status,
    int SequenceNo,
    int? ParallelGroupNo,
    string? BomReference,
    string? RoutingReference,
    string? WorkCenterCode,
    long ProducedStockId,
    string ProducedStockCode,
    string? ProducedStockName,
    long? ProducedYapCodeId,
    string? ProducedYapCode,
    string UnitCode,
    decimal PlannedQuantity,
    decimal CompletedQuantity,
    decimal ScrapQuantity,
    long SourceWarehouseId,
    long TargetWarehouseId,
    bool RequireMaterialTransferBeforeStart,
    DateTimeOffset? PlannedStartAtUtc,
    DateTimeOffset? PlannedEndAtUtc,
    IReadOnlyList<ProductionMaterialDto> Materials,
    IReadOnlyList<ProductionOutputDto> Outputs,
    IReadOnlyList<ProductionAssignmentDto> Assignments);

public sealed record ProductionDependencyDto(
    long Id,
    long PredecessorOrderId,
    long SuccessorOrderId,
    ProductionDependencyType DependencyType,
    int LagMinutes,
    bool RequireOutputAvailable,
    bool RequireTransferCompleted);

public sealed record ProductionPlanDetail(
    ProductionPlanGridRow Header,
    string RowVersion,
    string? Description,
    IReadOnlyList<ProductionOrderDto> Orders,
    IReadOnlyList<ProductionDependencyDto> Dependencies);

public sealed record ProductionTransitionRequest(string RowVersion,string? Reason);

public interface IProductionService
{
    Task<IReadOnlyList<ProductionSourceWorkOrderRow>> GetSourceWorkOrdersAsync(string? search,string branchCode,int take=200,CancellationToken ct=default);
    Task<IReadOnlyList<ProductionReturnedWorkOrderRow>> GetReturnedSourceWorkOrdersAsync(string? search,string branchCode,int take=200,CancellationToken ct=default);
    Task<PreparedNetsisProductionWorkOrder> PrepareSourceWorkOrderAsync(
        string workOrderNumber,
        ProductionOrderSourceType? sourceType,
        string? sourceSystemCode,
        string branchCode,
        long? transferId = null,
        long? kalanTaskId = null,
        CancellationToken ct = default);
    Task<PreparedNetsisProductionWorkOrder> PrepareNetsisWorkOrderAsync(string workOrderNumber,string branchCode,CancellationToken ct=default);
    Task<CreateProductionPlanResult> CreateAsync(CreateProductionPlanRequest request,long actor,CancellationToken ct=default);
    Task<PagedResponse<ProductionPlanGridRow>> GetPagedAsync(PagedRequest request,CancellationToken ct=default);
    Task<ProductionPlanDetail> GetDetailAsync(long id,CancellationToken ct=default);
    Task<ProductionPlanDetail> ReleaseAsync(long id,ProductionTransitionRequest request,long actor,CancellationToken ct=default);
    Task DeleteDraftAsync(long id,long actor,CancellationToken ct=default);
    Task<IReadOnlyList<ProductionSourceWorkOrderRow>> GetCancelledWorkOrderAssignmentsAsync(string? search,string branchCode,int take=200,CancellationToken ct=default);
    Task<ProductionWorkOrderAssignmentCancellationResult> CancelWorkOrderAssignmentAsync(CancelProductionWorkOrderAssignmentRequest request,string branchCode,long actor,CancellationToken ct=default);
    Task<ProductionWorkOrderAssignmentCancellationResult> RestoreWorkOrderAssignmentAsync(RestoreProductionWorkOrderAssignmentRequest request,string branchCode,long actor,CancellationToken ct=default);
}
