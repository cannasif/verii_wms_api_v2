namespace verii_wms_api_v2.Modules.WarehouseOutbound.Application;

public sealed record WarehouseOutboundOperationLineRequest(
    long LineId,
    decimal Quantity,
    long? SourceLocationId,
    long? TargetLocationId,
    string? LotNo,
    string? SerialNo,
    string? HandlingUnitNo);

public sealed record WarehouseOutboundOperationRequest(
    Guid IdempotencyKey,
    IReadOnlyList<WarehouseOutboundOperationLineRequest> Lines,
    DateTimeOffset? OccurredAtUtc,
    string? Reason,
    string? VehiclePlate,
    string? DriverName,
    string? WaybillNo,
    string? TrackingNo);

public sealed record WarehouseOutboundTransitionRequest(Guid IdempotencyKey, string? Reason);

public sealed record WarehouseOutboundOperationResult(
    long WarehouseOutboundId,
    string DocumentNo,
    string Status,
    long? StockMovementOperationId,
    decimal PickedQuantity,
    decimal PackedQuantity,
    decimal LoadedQuantity,
    decimal ShippedQuantity,
    bool Replayed);

public interface IWarehouseOutboundOperationService
{
    Task<WarehouseOutboundOperationResult> ApproveAsync(long id, WarehouseOutboundTransitionRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseOutboundOperationResult> ReleaseAsync(long id, WarehouseOutboundTransitionRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseOutboundOperationResult> PickAsync(long id, WarehouseOutboundOperationRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseOutboundOperationResult> PackAsync(long id, WarehouseOutboundOperationRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseOutboundOperationResult> LoadAsync(long id, WarehouseOutboundOperationRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseOutboundOperationResult> ShipAsync(long id, WarehouseOutboundOperationRequest request, long actor, CancellationToken ct = default);
    Task<WarehouseOutboundOperationResult> CancelAsync(long id, WarehouseOutboundTransitionRequest request, long actor, CancellationToken ct = default);
}
