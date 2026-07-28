namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record ReceiveGoodsReceiptTaskRequest(Guid IdempotencyKey, long TaskLineId, string Barcode,
    decimal? Quantity, string? LotNo, string? SerialNo, DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate, long? ToLocationId, DateTimeOffset? OccurredAtUtc, string? DeviceId);

public sealed record ReceiveGoodsReceiptTaskResult(long ExecutionId, long StockMovementOperationId,
    long GoodsReceiptId, long TaskId, long TaskLineId, decimal ProcessedQuantity,
    decimal RemainingQuantity, string TaskStatus, string LineStatus, long? QualityInspectionId,
    long? ConsumedLabelId, long? GeneratedLabelId, bool Replayed);

public interface IGoodsReceiptExecutionService
{
    Task<ReceiveGoodsReceiptTaskResult> ReceiveAsync(long taskId, ReceiveGoodsReceiptTaskRequest request,
        long actor, CancellationToken ct = default);
}
