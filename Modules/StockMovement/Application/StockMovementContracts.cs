using System.Text.Json.Serialization;
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

public sealed record StockMovementGridRow
{
    public long Id { get; init; }
    public Guid OperationCode { get; init; }
    public string OperationType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ReferenceType { get; init; }
    public string? ReferenceNo { get; init; }
    public DateTime OccurredAt { get; init; }
    public int EntryCount { get; init; }
    public decimal InboundQuantity { get; init; }
    public decimal OutboundQuantity { get; init; }
    public string? Reason { get; init; }
    public long? ReversalOfOperationId { get; init; }
    public long? CreatedBy { get; init; }
    public DateTime? CreatedDate { get; init; }
    public long? UpdatedBy { get; init; }
    public DateTime? UpdatedDate { get; init; }
    [JsonIgnore] public string ReferenceSearchText { get; init; } = string.Empty;
}

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
    Task ValidateAsync(PostStockMovementRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task<StockMovementPostResult> PostAsync(PostStockMovementRequest request, CancellationToken cancellationToken = default);
    Task<StockMovementPostResult> ReverseAsync(long operationId, ReverseStockMovementRequest request, CancellationToken cancellationToken = default);
}
