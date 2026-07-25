using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.StockMovement.Application;

public sealed record StockMovementLineRequest(
    long StockId,
    long? YapCodeId,
    decimal Quantity,
    long? SourceWarehouseId,
    long? SourceLocationId,
    long? TargetWarehouseId,
    long? TargetLocationId,
    string? UnitCode,
    string? LotNo,
    string? SerialNo,
    string? StockStatus,
    string? SourceStockStatus = null,
    string? TargetStockStatus = null);

public sealed record PostStockMovementRequest(
    string IdempotencyKey,
    string OperationType,
    string? ReferenceType,
    string? ReferenceNo,
    long? ReferenceId,
    DateTime? OccurredAt,
    string? Reason,
    string? Description,
    IReadOnlyList<StockMovementLineRequest> Lines);

public sealed record ReverseStockMovementRequest(string IdempotencyKey, string Reason, DateTime? OccurredAt);
public sealed record StockMovementPostResult(long OperationId, Guid OperationCode, bool IsReplay, int EntryCount);

public sealed record StockMovementGridRow(
    long Id, Guid OperationCode, string OperationType, string Status, string? ReferenceType, string? ReferenceNo,
    DateTime OccurredAt, int EntryCount, decimal InboundQuantity, decimal OutboundQuantity, string? Reason,
    long? ReversalOfOperationId, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);

public sealed record StockMovementEntryRow(
    long Id, int LineNo, long StockId, string StockCode, string StockName, long? YapCodeId, string? YapCode, long WarehouseId, int WarehouseCode,
    string WarehouseName, long LocationId, string LocationCode, string LocationName, decimal QuantityDelta,
    string UnitCode, string? LotNo, string? SerialNo, string StockStatus, DateTime OccurredAt);

public sealed record StockMovementDetail(
    long Id, Guid OperationCode, string IdempotencyKey, string OperationType, string Status, string? ReferenceType,
    string? ReferenceNo, long? ReferenceId, DateTime OccurredAt, string? Reason, string? Description,
    long? ReversalOfOperationId, long? CreatedBy, DateTime? CreatedDate, IReadOnlyList<StockMovementEntryRow> Entries);

public interface IStockMovementService
{
    Task<PagedResponse<StockMovementGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<StockMovementDetail> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<StockMovementPostResult> PostAsync(PostStockMovementRequest request, CancellationToken cancellationToken = default);
    Task<StockMovementPostResult> ReverseAsync(long operationId, ReverseStockMovementRequest request, CancellationToken cancellationToken = default);
}
