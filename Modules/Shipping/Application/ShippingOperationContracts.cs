namespace verii_wms_api_v2.Modules.Shipping.Application;

public sealed record ShipmentOperationLineRequest(
    long LineId,
    decimal Quantity,
    long? SourceLocationId,
    long? TargetLocationId,
    string? LotNo,
    string? SerialNo,
    string? HandlingUnitNo);

public sealed record ShipmentOperationRequest(
    Guid IdempotencyKey,
    IReadOnlyList<ShipmentOperationLineRequest> Lines,
    DateTimeOffset? OccurredAtUtc,
    string? Reason,
    string? VehiclePlate,
    string? DriverName,
    string? WaybillNo,
    string? TrackingNo);

public sealed record ShipmentTransitionRequest(Guid IdempotencyKey, string? Reason);

public sealed record ShipmentOperationResult(
    long ShipmentId,
    string DocumentNo,
    string Status,
    long? StockMovementOperationId,
    decimal PickedQuantity,
    decimal PackedQuantity,
    decimal LoadedQuantity,
    decimal ShippedQuantity,
    bool Replayed);

public interface IShippingOperationService
{
    Task<ShipmentOperationResult> ApproveAsync(long id, ShipmentTransitionRequest request, long actor, CancellationToken ct = default);
    Task<ShipmentOperationResult> ReleaseAsync(long id, ShipmentTransitionRequest request, long actor, CancellationToken ct = default);
    Task<ShipmentOperationResult> PickAsync(long id, ShipmentOperationRequest request, long actor, CancellationToken ct = default);
    Task<ShipmentOperationResult> PackAsync(long id, ShipmentOperationRequest request, long actor, CancellationToken ct = default);
    Task<ShipmentOperationResult> LoadAsync(long id, ShipmentOperationRequest request, long actor, CancellationToken ct = default);
    Task<ShipmentOperationResult> ShipAsync(long id, ShipmentOperationRequest request, long actor, CancellationToken ct = default);
    Task<ShipmentOperationResult> CancelAsync(long id, ShipmentTransitionRequest request, long actor, CancellationToken ct = default);
}
