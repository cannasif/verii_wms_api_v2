using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed record PlanWarehouseInboundTrackingRequest(
    decimal Quantity, string? LotNo, string? SerialNo,
    DateOnly? ManufacturingDate, DateOnly? ExpirationDate, string? Description);

public sealed record ReserveWarehouseInboundOrderLineRequest(
    string OrderNumber, int OrderId, decimal Quantity,
    long TargetWarehouseId, long ReceivingLocationId,
    StockTrackingType TrackingType,
    IReadOnlyList<PlanWarehouseInboundTrackingRequest>? Trackings);

public sealed record CreateOrderBasedWarehouseInboundRequest(
    Guid IdempotencyKey,
    string BranchCode,
    long DocumentSeriesId,
    long SupplierId,
    long TargetWarehouseId,
    long ReceivingLocationId,
    DateOnly DocumentDate,
    DateTimeOffset? PlannedArrivalAtUtc,
    WarehouseInboundLabelStrategy LabelStrategy,
    bool AllowOverReceipt,
    decimal OverReceiptTolerancePercent,
    bool AllowUnderReceipt,
    bool RequireQualityControl,
    bool RequirePutaway,
    byte Priority,
    string? Description,
    IReadOnlyList<long>? AssignedUserIds,
    IReadOnlyList<ReserveWarehouseInboundOrderLineRequest> Lines);

public sealed record CreateWarehouseInboundResult(
    long Id,
    string DocumentNo,
    long TaskId,
    string TaskNo,
    int LineCount,
    decimal ReservedQuantity,
    bool Replayed,
    IReadOnlyList<CreatedWarehouseInboundTaskResult> Tasks);

public sealed record CreatedWarehouseInboundTaskResult(long Id, string TaskNo, long WarehouseId, int LineCount, decimal PlannedQuantity);

public sealed record WarehouseInboundOrderSourceLine(
    string OrderNumber,
    int OrderId,
    string? StockCode,
    string? StockName,
    string? UnitCode,
    string? YapCode,
    string? YapDescription,
    string? CustomerCode,
    string? CustomerName,
    int? BranchCode,
    int? TargetWarehouseCode,
    DateTime? OrderDate,
    decimal OrderedQuantity,
    decimal DeliveredQuantity,
    decimal RemainingQuantity,
    decimal PlannedQuantity,
    decimal AvailableQuantity);

public interface IWarehouseInboundOrderSource
{
    Task<IReadOnlyList<WarehouseInboundOrderSourceLine>> GetOpenLinesAsync(string orderNumbersCsv, string customerCode, string branchCode, CancellationToken cancellationToken = default);
}

public interface IWarehouseInboundService
{
    Task<CreateWarehouseInboundResult> CreateFromOrdersAsync(CreateOrderBasedWarehouseInboundRequest request, long actorUserId, CancellationToken cancellationToken = default);
}
