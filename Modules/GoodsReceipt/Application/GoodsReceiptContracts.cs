using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record PlanGoodsReceiptTrackingRequest(
    decimal Quantity, string? LotNo, string? SerialNo,
    DateOnly? ManufacturingDate, DateOnly? ExpirationDate, string? Description);

public sealed record ReserveGoodsReceiptOrderLineRequest(
    string OrderNumber, int OrderId, decimal Quantity,
    long TargetWarehouseId, long ReceivingLocationId,
    StockTrackingType TrackingType,
    IReadOnlyList<PlanGoodsReceiptTrackingRequest>? Trackings,
    bool ForceQualityControl = false);

public sealed record CreateOrderBasedGoodsReceiptRequest(
    Guid IdempotencyKey,
    string BranchCode,
    long DocumentSeriesId,
    long SupplierId,
    long TargetWarehouseId,
    long ReceivingLocationId,
    DateOnly DocumentDate,
    string? WaybillNo,
    DateOnly? WaybillDate,
    string? ElectronicWaybillNo,
    DateTimeOffset? PlannedArrivalAtUtc,
    GoodsReceiptLabelStrategy LabelStrategy,
    bool AllowOverReceipt,
    decimal OverReceiptTolerancePercent,
    bool AllowUnderReceipt,
    bool RequireQualityControl,
    bool RequirePutaway,
    byte Priority,
    string? Description,
    IReadOnlyList<long>? AssignedUserIds,
    IReadOnlyList<ReserveGoodsReceiptOrderLineRequest> Lines,
    bool ForceQualityControl = false);

public sealed record CreateGoodsReceiptResult(
    long Id,
    string DocumentNo,
    long TaskId,
    string TaskNo,
    int LineCount,
    decimal ReservedQuantity,
    bool Replayed,
    IReadOnlyList<CreatedGoodsReceiptTaskResult> Tasks);

public sealed record CreatedGoodsReceiptTaskResult(long Id, string TaskNo, long WarehouseId, int LineCount, decimal PlannedQuantity);

public sealed record GoodsReceiptOrderSourceLine(
    string OrderNumber,
    int OrderId,
    int OrderLineSequence,
    string? StockCode,
    string? StockName,
    string? UnitCode,
    string? YapCode,
    string? YapDescription,
    string? CustomerCode,
    string? CustomerName,
    int? BranchCode,
    int? TargetWarehouseCode,
    string? ProjectCode,
    DateTime? OrderDate,
    DateTime? DeliveryDate,
    decimal NetUnitPrice,
    decimal GrossUnitPrice,
    decimal OrderedQuantity,
    decimal DeliveredQuantity,
    decimal RemainingQuantity,
    decimal PlannedQuantity,
    decimal AvailableQuantity);

public interface IGoodsReceiptOrderSource
{
    Task<IReadOnlyList<GoodsReceiptOrderSourceLine>> GetOpenLinesAsync(string orderNumbersCsv, string customerCode, string branchCode, CancellationToken cancellationToken = default);
}

public interface IGoodsReceiptService
{
    Task<CreateGoodsReceiptResult> CreateFromOrdersAsync(CreateOrderBasedGoodsReceiptRequest request, long actorUserId, CancellationToken cancellationToken = default);
}
