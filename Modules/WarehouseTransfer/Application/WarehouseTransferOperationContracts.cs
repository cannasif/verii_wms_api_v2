namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

public sealed record WarehouseTransferOperationLineRequest(
    long LineId,
    decimal Quantity,
    long? SourceLocationId,
    long? TargetLocationId,
    string? LotNo,
    string? SerialNo);

public sealed record WarehouseTransferOperationRequest(
    Guid IdempotencyKey,
    IReadOnlyList<WarehouseTransferOperationLineRequest> Lines,
    DateTimeOffset? OccurredAtUtc,
    string? Reason,
    string? VehiclePlate,
    string? DriverName,
    string? WaybillNo);

public sealed record WarehouseTransferTransitionRequest(Guid IdempotencyKey, string? Reason);

public sealed record WarehouseTransferOperationResult(
    long TransferId,
    string DocumentNo,
    string Status,
    long? StockMovementOperationId,
    decimal PickedQuantity,
    decimal ShippedQuantity,
    decimal ReceivedQuantity,
    decimal PutawayQuantity,
    bool Replayed);

public interface IWarehouseTransferOperationService
{
    Task<WarehouseTransferOperationResult> ApproveAsync(long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseTransferOperationResult> ReleaseAsync(long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseTransferOperationResult> PickAsync(long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseTransferOperationResult> DispatchAsync(long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseTransferOperationResult> ReceiveAsync(long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseTransferOperationResult> PutawayAsync(long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseTransferOperationResult> CancelAsync(long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default);
}
