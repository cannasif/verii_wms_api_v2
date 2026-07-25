using verii_wms_api_v2.Modules.WarehouseOperations.Domain;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed record WarehouseInboundTransitionRequest(
    Guid IdempotencyKey,
    string? Reason,
    string RowVersion);

public sealed record WarehouseInboundShortCloseLineRequest(
    long LineId,
    decimal Quantity);

public sealed record ShortCloseWarehouseInboundRequest(
    Guid IdempotencyKey,
    string Reason,
    string RowVersion,
    IReadOnlyList<WarehouseInboundShortCloseLineRequest> Lines);

public sealed record WarehouseInboundPutawayLineRequest(
    long LineId,
    decimal Quantity,
    long? SourceLocationId,
    long TargetLocationId,
    string? LotNo,
    string? SerialNo);

public sealed record PutawayWarehouseInboundRequest(
    Guid IdempotencyKey,
    string? Reason,
    string RowVersion,
    DateTimeOffset? OccurredAtUtc,
    IReadOnlyList<WarehouseInboundPutawayLineRequest> Lines);

public sealed record WarehouseInboundLifecycleResult(
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

public interface IWarehouseInboundLifecycleService
{
    Task<WarehouseInboundLifecycleResult> ApproveAsync(
        long id,
        WarehouseInboundTransitionRequest request,
        long actor,
        CancellationToken cancellationToken = default);

    Task<WarehouseInboundLifecycleResult> ShortCloseAsync(
        long id,
        ShortCloseWarehouseInboundRequest request,
        long actor,
        CancellationToken cancellationToken = default);

    Task<WarehouseInboundLifecycleResult> PutawayAsync(
        long id,
        PutawayWarehouseInboundRequest request,
        long actor,
        CancellationToken cancellationToken = default);

    Task<WarehouseInboundLifecycleResult> CancelAsync(
        long id,
        WarehouseInboundTransitionRequest request,
        long actor,
        CancellationToken cancellationToken = default);
}
