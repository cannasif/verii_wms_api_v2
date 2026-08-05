using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed record ProductionTransferTaskAssignmentDto(long UserId, string Username, bool IsPrimary, DateTimeOffset AssignedAtUtc, DateTimeOffset? AcceptedAtUtc);
public sealed record ProductionTransferTaskLineDto(
    long TaskLineId, long TransferLineId, string StockCode, string? StockName,
    decimal RequestedQuantity, decimal ReservedQuantity, decimal MissingQuantity, decimal ProcessedQuantity,
    long? SourceLocationId, string? SourceLocationCode, string? SourceLocationName,
    decimal TotalRequestedQuantity);
public sealed record ProductionTransferTaskDto(
    long TaskId, string TaskNo, WarehouseTransferTaskType TaskType, long WarehouseId, WarehouseTransferTaskStatus Status,
    DateTimeOffset? AcceptedAtUtc, long? AcceptedBy, DateTimeOffset? StartedAtUtc, long? StartedBy,
    DateTimeOffset? CompletedAtUtc, long? CompletedBy,
    long? OriginTaskId, long? OriginUserId, long? PreviousTaskId,
    IReadOnlyList<ProductionTransferTaskAssignmentDto> Assignments,
    IReadOnlyList<ProductionTransferTaskLineDto> Lines);
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
    IReadOnlyList<ProductionTransferAssigneeOptionDto> EligibleAssignees);
public sealed record AssignProductionTransferTaskRequest(long UserId, bool IsPrimary = false);
public sealed record HandoffProductionTransferTaskRequest(long TargetUserId, string? Reason);
public sealed record StartProductionTransferTaskRequest(Guid IdempotencyKey);
public sealed record WarehouseTransferReturnSettingDto(
    long WarehouseId,
    long? DefaultTransferReturnLocationId,
    long? DefaultProductionTransferLocationId);
public sealed record UpdateWarehouseTransferReturnSettingRequest(
    long WarehouseId,
    long? DefaultTransferReturnLocationId,
    long? DefaultProductionTransferLocationId);

public interface IProductionTransferTaskService
{
    Task<ProductionTransferTaskBoardDto> GetBoardAsync(long transferId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductionTransferTaskPoolRow>> GetPoolAsync(long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> AssignAsync(long transferId, long taskId, AssignProductionTransferTaskRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> RemoveAssignmentAsync(long transferId, long taskId, long userId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> RequestAssignmentReturnAsync(long transferId, long taskId, long userId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> CompleteAssignmentReturnAsync(long transferId, long taskId, Guid idempotencyKey, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> HandoffAsync(long transferId, long taskId, HandoffProductionTransferTaskRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> RefreshRouteAsync(long transferId, long taskId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> AcceptAndStartAsync(long transferId, long taskId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> CompleteCancellationReturnAsync(long transferId, long taskId, Guid idempotencyKey, long actor, CancellationToken ct = default);
    Task<WarehouseTransferReturnSettingDto> GetReturnSettingAsync(long warehouseId, CancellationToken ct = default);
    Task<WarehouseTransferReturnSettingDto> UpdateReturnSettingAsync(UpdateWarehouseTransferReturnSettingRequest request, long actor, CancellationToken ct = default);
}
