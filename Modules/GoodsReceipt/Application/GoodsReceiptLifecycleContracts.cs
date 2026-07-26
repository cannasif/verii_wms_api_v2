using verii_wms_api_v2.Modules.WarehouseOperations.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record GoodsReceiptTransitionRequest(
    Guid IdempotencyKey,
    string? Reason,
    string RowVersion);

public sealed record GoodsReceiptShortCloseLineRequest(
    long LineId,
    decimal Quantity);

public sealed record ShortCloseGoodsReceiptRequest(
    Guid IdempotencyKey,
    string Reason,
    string RowVersion,
    IReadOnlyList<GoodsReceiptShortCloseLineRequest> Lines);

public sealed record GoodsReceiptPutawayLineRequest(
    long LineId,
    decimal Quantity,
    long? SourceLocationId,
    long TargetLocationId,
    string? LotNo,
    string? SerialNo);

public sealed record PutawayGoodsReceiptRequest(
    Guid IdempotencyKey,
    string? Reason,
    string RowVersion,
    DateTimeOffset? OccurredAtUtc,
    IReadOnlyList<GoodsReceiptPutawayLineRequest> Lines);

public sealed record GoodsReceiptLifecycleResult(
    long Id,
    string DocumentNo,
    WarehouseOperationStatus Status,
    OperationApprovalStatus ApprovalStatus,
    OperationQualityStatus QualityStatus,
    OperationPutawayStatus PutawayStatus,
    long? StockMovementOperationId,
    decimal AffectedQuantity,
    bool Replayed,
    string RowVersion);

public interface IGoodsReceiptLifecycleService
{
    Task<GoodsReceiptLifecycleResult> ApproveAsync(
        long id,
        GoodsReceiptTransitionRequest request,
        long actor,
        CancellationToken cancellationToken = default);

    Task<GoodsReceiptLifecycleResult> ShortCloseAsync(
        long id,
        ShortCloseGoodsReceiptRequest request,
        long actor,
        CancellationToken cancellationToken = default);

    Task<GoodsReceiptLifecycleResult> PutawayAsync(
        long id,
        PutawayGoodsReceiptRequest request,
        long actor,
        CancellationToken cancellationToken = default);

    Task<GoodsReceiptLifecycleResult> CancelAsync(
        long id,
        GoodsReceiptTransitionRequest request,
        long actor,
        CancellationToken cancellationToken = default);

    Task<GoodsReceiptLifecycleResult> CancelAfterErpDeletionAsync(
        long id,
        GoodsReceiptTransitionRequest request,
        long actor,
        CancellationToken cancellationToken = default);
}
