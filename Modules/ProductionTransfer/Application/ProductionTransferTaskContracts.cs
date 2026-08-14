using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed record ProductionTransferTaskAssignmentDto(long UserId, string Username, bool IsPrimary, DateTimeOffset AssignedAtUtc, DateTimeOffset? AcceptedAtUtc);
public sealed record ProductionTransferTaskLineDto(
    long TaskLineId, long TransferLineId, string StockCode, string? StockName,
    decimal RequestedQuantity, decimal ReservedQuantity, decimal MissingQuantity, decimal ProcessedQuantity,
    long? SourceLocationId, string? SourceLocationCode, string? SourceLocationName,
    long? TargetLocationId, string? TargetLocationCode, string? TargetLocationName,
    string? SerialNo,
    decimal TotalRequestedQuantity);
public sealed record ProductionTransferTaskDto(
    long TaskId, string TaskNo, WarehouseTransferTaskType TaskType, long WarehouseId, WarehouseTransferTaskStatus Status,
    DateTimeOffset? AcceptedAtUtc, long? AcceptedBy, DateTimeOffset? StartedAtUtc, long? StartedBy,
    DateTimeOffset? CompletedAtUtc, long? CompletedBy,
    long? OriginTaskId, long? OriginUserId, long? PreviousTaskId,
    IReadOnlyList<ProductionTransferTaskAssignmentDto> Assignments,
    IReadOnlyList<ProductionTransferTaskLineDto> Lines,
    IReadOnlyList<string> AssignedUsernames);
public sealed record ProductionTransferWorkloadDto(
    long UserId, string Username, int AssignedTaskCount, int CompletedTaskCount,
    decimal PlannedQuantity, decimal ProcessedQuantity, decimal CompletionPercent);
public sealed record ProductionTransferTaskPoolRow(
    long TransferId, string DocumentNo, WarehouseTransferBusinessContext BusinessContext,
    WarehouseTransferStatus TransferStatus, long TaskId, string TaskNo, WarehouseTransferTaskType TaskType,
    long WarehouseId, WarehouseTransferTaskStatus TaskStatus, decimal PlannedQuantity,
    decimal ProcessedQuantity, decimal RemainingQuantity, IReadOnlyList<string> AssignedUsers,
    DateTime? CreatedDate);
public sealed record ProductionTransferAssigneeOptionDto(long UserId, string Username, IReadOnlyList<long> WarehouseIds);
public sealed record ProductionTransferTaskBoardDto(
    long TransferId, string DocumentNo, WarehouseTransferStatus TransferStatus, long SourceWarehouseId,
    IReadOnlyList<ProductionTransferTaskDto> Tasks,
    IReadOnlyList<ProductionTransferWorkloadDto> Workloads,
    IReadOnlyList<ProductionTransferAssigneeOptionDto> EligibleAssignees,
    bool SourceIsRackless = false,
    bool TargetIsRackless = false);
public sealed record AssignProductionTransferTaskRequest(long UserId, bool IsPrimary = false);
public sealed record ReleaseProductionTransferTaskToPoolRequest(long WarehouseId);
public sealed record ClaimProductionTransferTaskRequest(Guid IdempotencyKey);
public sealed record HandoffProductionTransferTaskRequest(long TargetUserId, string? Reason);
public sealed record StartProductionTransferTaskRequest(Guid IdempotencyKey, bool AllowPartialStart = false);
public sealed record ProcessProductionReturnLineRequest(Guid IdempotencyKey, long TargetLocationId);
public sealed record CompleteProductionReturnLineRequest(long TaskLineId, long TargetLocationId);
public sealed record CompleteProductionReturnRequest(
    Guid IdempotencyKey,
    IReadOnlyList<CompleteProductionReturnLineRequest> Lines);

public sealed record ProductionTaskStockShortageDto(
    long TaskLineId,
    long TransferLineId,
    string StockCode,
    string? StockName,
    decimal RequestedQuantity,
    decimal AvailableQuantity,
    decimal ShortageQuantity);

public sealed record ProductionTaskStartCheckDto(bool CanStartFully, IReadOnlyList<ProductionTaskStockShortageDto> Shortages);
public sealed record WarehouseTransferReturnSettingDto(
    long WarehouseId,
    long? DefaultProductionTransferReturnLocationId,
    long? DefaultProductionTransferLocationId,
    long? ProductionPickingStagingLocationId,
    decimal? AutoPickWithoutConfirmMaxQuantity,
    bool IsRackless = false)
{
    // Eski web paketi API'den önce/sonra kısa süre çalışırsa ayar ekranı kırılmasın.
    // Değer artık genel DAT kolonundan değil üretime özel kolondan gelir.
    [Obsolete("Use DefaultProductionTransferReturnLocationId.")]
    public long? DefaultTransferReturnLocationId => DefaultProductionTransferReturnLocationId;
}
public sealed record UpdateWarehouseTransferReturnSettingRequest(
    long WarehouseId,
    long? DefaultProductionTransferReturnLocationId,
    long? DefaultProductionTransferLocationId,
    long? ProductionPickingStagingLocationId,
    decimal? AutoPickWithoutConfirmMaxQuantity,
    long? DefaultTransferReturnLocationId = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long? ResolvedProductionTransferReturnLocationId =>
        DefaultProductionTransferReturnLocationId ?? DefaultTransferReturnLocationId;
}

public sealed record ProductionWorkOrderTransferTaskRowDto(
    long TaskId,
    string TaskNo,
    string DisplayLabel,
    string? DisplaySuffix,
    WarehouseTransferTaskType TaskType,
    WarehouseTransferTaskStatus Status,
    long WarehouseId,
    decimal PlannedQuantity,
    decimal ProcessedQuantity,
    decimal RemainingQuantity,
    IReadOnlyList<string> AssignedUsernames,
    long? PreviousTaskId,
    long? OriginTaskId,
    long? OriginUserId,
    DateTimeOffset? CompletedAtUtc);

public sealed record ProductionWorkOrderTransferHeaderRowDto(
    long TransferId,
    string DocumentNo,
    string? ExternalReferenceNo,
    WarehouseTransferStatus TransferStatus,
    ProductionTransferWorkflowStatus WorkflowStatus,
    long? ProductionOrderId,
    string? ProductionOrderNo,
    long? ProductionHeaderId,
    long? ParentTransferId,
    long? ResidualTransferId,
    string? ResidualDocumentNo,
    bool IsResidualHeader,
    long SourceWarehouseId,
    int SourceWarehouseCode,
    string SourceWarehouseName,
    long TargetWarehouseId,
    int TargetWarehouseCode,
    string TargetWarehouseName,
    decimal RequestedQuantity,
    decimal PickedQuantity,
    DateOnly DocumentDate,
    WarehouseTransferInitiationMode InitiationMode,
    int LineCount,
    decimal ShippedQuantity,
    decimal ReceivedQuantity,
    decimal PutawayQuantity,
    long? CreatedBy,
    DateTime? CreatedDate,
    long? UpdatedBy,
    DateTime? UpdatedDate,
    ProductionTransferErpPostingPolicy ErpPostingPolicy,
    ErpIntegrationStatus ErpIntegrationStatus,
    ErpPostingStatus? ErpPostingStatus,
    string? ErpDocumentNo,
    string? ErpErrorCode,
    string? ErpErrorMessage,
    IReadOnlyList<ProductionWorkOrderTransferTaskRowDto> Tasks,
    bool SourceIsRackless = false,
    bool TargetIsRackless = false);

public interface IProductionTransferTaskService
{
    Task<ProductionTransferTaskBoardDto> GetBoardAsync(long transferId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductionTransferAssigneeOptionDto>> GetEligibleAssigneesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductionTransferTaskPoolRow>> GetPoolAsync(long actor, CancellationToken ct = default);
    Task<IReadOnlyList<ProductionWorkOrderTransferHeaderRowDto>> GetWorkOrderTransferGroupsAsync(
        ProductionWorkOrderTransferTab tab,
        string? search,
        long actor,
        CancellationToken ct = default);
    Task<IReadOnlyList<ProductionWorkOrderTransferTaskRowDto>> GetWorkOrderTransferHeaderTasksAsync(
        long transferId,
        CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> AssignAsync(long transferId, long taskId, AssignProductionTransferTaskRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> ReleaseToPoolAsync(long transferId, long taskId, ReleaseProductionTransferTaskToPoolRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> ClaimTaskAsync(long transferId, long taskId, ClaimProductionTransferTaskRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> RemoveAssignmentAsync(long transferId, long taskId, long userId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> RequestCancellationReturnAsync(long transferId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> ProcessReturnTaskLineAsync(long transferId, long taskId, long taskLineId, ProcessProductionReturnLineRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> HandoffAsync(long transferId, long taskId, HandoffProductionTransferTaskRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> RefreshRouteAsync(long transferId, long taskId, long actor, CancellationToken ct = default);
    Task<ProductionTaskStartCheckDto> CheckStartAsync(long transferId, long taskId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> AcceptAndStartAsync(long transferId, long taskId, long actor, bool allowPartialStart = false, CancellationToken ct = default);
    Task ApplyPermanentRouteSplitAsync(long transferId, long taskId, long actor, CancellationToken ct = default);
    Task<IReadOnlyList<WarehouseTransferPickedSourceLocationDto>> GetLinePickedSourcesAsync(long transferId, long lineId, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> CompleteCancellationReturnAsync(long transferId, long taskId, CompleteProductionReturnRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseTransferReturnSettingDto> GetReturnSettingAsync(long warehouseId, CancellationToken ct = default);
    Task<WarehouseTransferReturnSettingDto> UpdateReturnSettingAsync(UpdateWarehouseTransferReturnSettingRequest request, long actor, CancellationToken ct = default);
}
