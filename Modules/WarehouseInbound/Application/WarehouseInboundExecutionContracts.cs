namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed record ReceiveWarehouseInboundTaskRequest(Guid IdempotencyKey, long TaskLineId, string Barcode,
    decimal? Quantity, string? LotNo, string? SerialNo, DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate, long? ToLocationId, DateTimeOffset? OccurredAtUtc, string? DeviceId);

public sealed record ReceiveWarehouseInboundTaskResult(long ExecutionId, long StockMovementOperationId,
    long WarehouseInboundId, long TaskId, long TaskLineId, decimal ProcessedQuantity,
    decimal RemainingQuantity, string TaskStatus, string LineStatus, long? QualityInspectionId,
    long? ConsumedLabelId, bool Replayed);

public interface IWarehouseInboundExecutionService
{
    Task<ReceiveWarehouseInboundTaskResult> ReceiveAsync(long taskId, ReceiveWarehouseInboundTaskRequest request,
        long actor, CancellationToken ct = default);
}
