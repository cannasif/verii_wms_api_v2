using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed record ProductionTransferTaskAssignmentDto(long UserId, string Username, bool IsPrimary, DateTimeOffset AssignedAtUtc, DateTimeOffset? AcceptedAtUtc);
public sealed record ProductionTransferTaskLineDto(
    long TaskLineId, long TransferLineId, string StockCode, string? StockName,
    decimal RequestedQuantity, decimal ReservedQuantity, decimal MissingQuantity, decimal ProcessedQuantity,
    long? SourceLocationId, string? SourceLocationCode, string? SourceLocationName);
public sealed record ProductionTransferTaskDto(
    long TaskId, string TaskNo, WarehouseTransferTaskType TaskType, long WarehouseId, WarehouseTransferTaskStatus Status,
    DateTimeOffset? AcceptedAtUtc, long? AcceptedBy, DateTimeOffset? StartedAtUtc, long? StartedBy,
    IReadOnlyList<ProductionTransferTaskAssignmentDto> Assignments,
    IReadOnlyList<ProductionTransferTaskLineDto> Lines);
public sealed record ProductionTransferWorkloadDto(long UserId, string Username, int AssignedTaskCount, int CompletedTaskCount, decimal CompletionPercent);
public sealed record ProductionTransferAssigneeOptionDto(long UserId, string Username, IReadOnlyList<long> WarehouseIds);
public sealed record ProductionTransferTaskBoardDto(
    long TransferId, string DocumentNo, WarehouseTransferStatus TransferStatus, long SourceWarehouseId,
    IReadOnlyList<ProductionTransferTaskDto> Tasks,
    IReadOnlyList<ProductionTransferWorkloadDto> Workloads,
    IReadOnlyList<ProductionTransferAssigneeOptionDto> EligibleAssignees);
public sealed record AssignProductionTransferTaskRequest(long UserId, bool IsPrimary = false);
public sealed record StartProductionTransferTaskRequest(Guid IdempotencyKey);
public sealed record WarehouseTransferReturnSettingDto(long WarehouseId, long? DefaultTransferReturnLocationId);
public sealed record UpdateWarehouseTransferReturnSettingRequest(long WarehouseId, long? DefaultTransferReturnLocationId);

public interface IProductionTransferTaskService
{
    Task<ProductionTransferTaskBoardDto> GetBoardAsync(long transferId, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> AssignAsync(long transferId, long taskId, AssignProductionTransferTaskRequest request, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> RemoveAssignmentAsync(long transferId, long taskId, long userId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> AcceptAndStartAsync(long transferId, long taskId, long actor, CancellationToken ct = default);
    Task<ProductionTransferTaskBoardDto> CompleteCancellationReturnAsync(long transferId, long taskId, Guid idempotencyKey, long actor, CancellationToken ct = default);
    Task<WarehouseTransferReturnSettingDto> GetReturnSettingAsync(long warehouseId, CancellationToken ct = default);
    Task<WarehouseTransferReturnSettingDto> UpdateReturnSettingAsync(UpdateWarehouseTransferReturnSettingRequest request, long actor, CancellationToken ct = default);
}
