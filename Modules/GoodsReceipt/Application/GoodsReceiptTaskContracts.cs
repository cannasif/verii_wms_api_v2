using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record GoodsReceiptTaskGridRow(
    long Id, long GoodsReceiptId, string BranchCode, string TaskNo, string DocumentNo,
    GoodsReceiptTaskType TaskType, GoodsReceiptTaskStatus Status,
    WarehouseOperationStatus ReceiptStatus, GoodsReceiptProcessType ProcessType,
    GoodsReceiptLabelStrategy LabelStrategy, byte Priority,
    long WarehouseId, int WarehouseCode, string WarehouseName,
    string? SupplierCode, string? SupplierName,
    int LineCount, decimal PlannedQuantity, decimal ProcessedQuantity,
    int AssigneeCount, GoodsReceiptAssignmentStatus? MyAssignmentStatus,
    DateTimeOffset? PlannedStartAtUtc, DateTimeOffset? DueAtUtc,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate,
    byte[] RowVersion);

public sealed record GoodsReceiptTaskAssignmentDto(
    long Id, long UserId, string Username, string DisplayName,
    GoodsReceiptAssignmentRole Role, GoodsReceiptAssignmentStatus Status,
    DateTimeOffset AssignedAtUtc, DateTimeOffset? AcceptedAtUtc,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc);

public sealed record GoodsReceiptTaskLineDto(
    long Id, int SequenceNo, long GoodsReceiptLineId, long StockId,
    string StockCode, string? StockName, string? YapCode,
    decimal PlannedQuantity, decimal ProcessedQuantity, string UnitCode,
    GoodsReceiptTaskStatus Status, long TargetWarehouseId, long? ToLocationId,
    StockTrackingType TrackingType, bool RequireQualityControl,
    IReadOnlyList<GoodsReceiptTaskLineTrackingDto> Trackings);

public sealed record GoodsReceiptTaskLineTrackingDto(
    long Id, int SequenceNo, decimal PlannedQuantity,
    string? LotNo, string? SerialNo,
    DateOnly? ManufacturingDate, DateOnly? ExpirationDate,
    long TargetWarehouseId, long ToLocationId, string? Description);

public sealed record GoodsReceiptTaskDetail(
    GoodsReceiptTaskGridRow Task,
    IReadOnlyList<GoodsReceiptTaskLineDto> Lines,
    IReadOnlyList<GoodsReceiptTaskAssignmentDto> Assignments);

public sealed record ReplaceGoodsReceiptTaskAssignmentsRequest(
    IReadOnlyList<long> UserIds, string RowVersion);

public interface IGoodsReceiptTaskService
{
    Task<PagedResponse<GoodsReceiptTaskGridRow>> GetPagedAsync(PagedRequest request, long? currentUserId, bool assignedOnly, CancellationToken cancellationToken = default);
    Task<GoodsReceiptTaskDetail> GetDetailAsync(long id, long currentUserId, CancellationToken cancellationToken = default);
    Task<GoodsReceiptTaskDetail> ReplaceAssignmentsAsync(long id, ReplaceGoodsReceiptTaskAssignmentsRequest request, long actorUserId, CancellationToken cancellationToken = default);
    Task<GoodsReceiptTaskDetail> AcceptAsync(long id, long actorUserId, CancellationToken cancellationToken = default);
    Task<GoodsReceiptTaskDetail> StartAsync(long id, long actorUserId, CancellationToken cancellationToken = default);
}
