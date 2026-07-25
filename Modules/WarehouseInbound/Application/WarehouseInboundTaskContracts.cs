using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed record WarehouseInboundTaskGridRow(
    long Id, long WarehouseInboundId, string BranchCode, string TaskNo, string DocumentNo,
    WarehouseInboundTaskType TaskType, WarehouseInboundTaskStatus Status,
    WarehouseOperationStatus ReceiptStatus, WarehouseInboundProcessType ProcessType, byte Priority,
    long WarehouseId, int WarehouseCode, string WarehouseName,
    string? SupplierCode, string? SupplierName,
    int LineCount, decimal PlannedQuantity, decimal ProcessedQuantity,
    int AssigneeCount, WarehouseInboundAssignmentStatus? MyAssignmentStatus,
    DateTimeOffset? PlannedStartAtUtc, DateTimeOffset? DueAtUtc,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate,
    byte[] RowVersion);

public sealed record WarehouseInboundTaskAssignmentDto(
    long Id, long UserId, string Username, string DisplayName,
    WarehouseInboundAssignmentRole Role, WarehouseInboundAssignmentStatus Status,
    DateTimeOffset AssignedAtUtc, DateTimeOffset? AcceptedAtUtc,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc);

public sealed record WarehouseInboundTaskLineDto(
    long Id, int SequenceNo, long WarehouseInboundLineId, long StockId,
    string StockCode, string? StockName, string? YapCode,
    decimal PlannedQuantity, decimal ProcessedQuantity, string UnitCode,
    WarehouseInboundTaskStatus Status, long TargetWarehouseId, long? ToLocationId,
    StockTrackingType TrackingType,
    IReadOnlyList<WarehouseInboundTaskLineTrackingDto> Trackings);

public sealed record WarehouseInboundTaskLineTrackingDto(
    long Id, int SequenceNo, decimal PlannedQuantity,
    string? LotNo, string? SerialNo,
    DateOnly? ManufacturingDate, DateOnly? ExpirationDate,
    long TargetWarehouseId, long ToLocationId, string? Description);

public sealed record WarehouseInboundTaskDetail(
    WarehouseInboundTaskGridRow Task,
    IReadOnlyList<WarehouseInboundTaskLineDto> Lines,
    IReadOnlyList<WarehouseInboundTaskAssignmentDto> Assignments);

public sealed record ReplaceWarehouseInboundTaskAssignmentsRequest(
    IReadOnlyList<long> UserIds, string RowVersion);

public interface IWarehouseInboundTaskService
{
    Task<PagedResponse<WarehouseInboundTaskGridRow>> GetPagedAsync(PagedRequest request, long? currentUserId, bool assignedOnly, CancellationToken cancellationToken = default);
    Task<WarehouseInboundTaskDetail> GetDetailAsync(long id, long currentUserId, CancellationToken cancellationToken = default);
    Task<WarehouseInboundTaskDetail> ReplaceAssignmentsAsync(long id, ReplaceWarehouseInboundTaskAssignmentsRequest request, long actorUserId, CancellationToken cancellationToken = default);
    Task<WarehouseInboundTaskDetail> AcceptAsync(long id, long actorUserId, CancellationToken cancellationToken = default);
    Task<WarehouseInboundTaskDetail> StartAsync(long id, long actorUserId, CancellationToken cancellationToken = default);
}
