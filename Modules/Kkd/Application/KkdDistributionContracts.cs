using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdBarcodeResolveRequest(string Barcode, long? WarehouseId);
public sealed record KkdMaterialRequestConfiguration(bool IsEnabled);

public sealed record KkdDistributionTrackingRequest(
    decimal Quantity,
    string? LotNo,
    string? SerialNo,
    string? HandlingUnitNo,
    DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate,
    long? SourceLocationId);

public sealed record KkdDistributionLineCreateRequest(
    long StockId,
    long? YapCodeId,
    decimal Quantity,
    string? UnitCode,
    long SourceLocationId,
    string? OrderNumber,
    long? OrderLineId,
    bool RequireHandlingUnit,
    string? Description,
    IReadOnlyList<KkdDistributionTrackingRequest>? Trackings,
    long? KkdRequestLineId = null);

public sealed record KkdDistributionCreateRequest(
    Guid IdempotencyKey,
    long EmployeeId,
    long WarehouseId,
    long DocumentSeriesId,
    DateOnly DocumentDate,
    long? StagingLocationId,
    long? LoadingLocationId,
    string? Description,
    IReadOnlyList<KkdDistributionLineCreateRequest> Lines,
    bool CreateWarehouseTask = false,
    IReadOnlyList<long>? AssignedUserIds = null,
    long? KkdRequestId = null);

public sealed record KkdDistributionCreateResult(
    long Id,
    string DocumentNo,
    string Status,
    long WarehouseOutboundId,
    string WarehouseOutboundDocumentNo,
    decimal TotalQuantity,
    decimal EntitledQuantity,
    decimal ExcessQuantity,
    string ExcessApprovalStatus,
    bool Replayed);

public sealed record KkdDistributionCompleteRequest(Guid IdempotencyKey);
public sealed record KkdDistributionCancelRequest(Guid IdempotencyKey, string Reason);
public sealed record KkdExcessApprovalRequest(Guid IdempotencyKey, bool Approve, string Reason);

public sealed record KkdDistributionCompleteResult(
    long Id,
    string DocumentNo,
    string Status,
    long WarehouseOutboundId,
    string WarehouseOutboundStatus,
    string ErpStatus,
    bool Replayed);

public sealed record KkdDistributionRow(
    long Id,
    string DocumentNo,
    string Status,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    long WarehouseId,
    long? WarehouseOutboundId,
    decimal TotalQuantity,
    decimal EntitledQuantity,
    decimal ExcessQuantity,
    string ExcessApprovalStatus,
    string? ExcessApprovalReason,
    long? ExcessApprovedBy,
    DateTimeOffset? ExcessApprovedAtUtc,
    DateTime? CreatedDate,
    DateTimeOffset? CompletedAtUtc);

public sealed record KkdDistributionLineDetail(
    long Id, int LineNo, long StockId, string StockCode, string StockName, string GroupCode,
    decimal Quantity, decimal EntitledQuantity, decimal ExcessQuantity, long SourceLocationId,
    string? LotNo, string? SerialNo, string? OpenOrderNo, string? OpenOrderLineId);

public sealed record KkdDistributionDetail(
    long Id, Guid CorrelationId, string DocumentNo, string Status,
    long EmployeeId, string EmployeeCode, string EmployeeName, long CustomerId, long WarehouseId,
    long? WarehouseOutboundId, string ExcessApprovalStatus, string? ExcessApprovalReason,
    string? FailureReason, DateTime? CreatedDate, DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<KkdDistributionLineDetail> Lines);

public sealed record KkdDistributionContext(
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string BranchCode,
    long CustomerId,
    string CustomerCode,
    string CustomerName,
    KkdPolicyDto Policy,
    IReadOnlyList<KkdOpenOrderHeader> Orders,
    IReadOnlyList<KkdPreferredStock> PreferredStocks);

public sealed record KkdPreferredStock(string GroupCode, long StockId, string StockCode, string StockName);

public sealed record KkdOpenOrderHeader(
    string OrderNumber,
    DateOnly? OrderDate,
    string? ProjectCode,
    decimal RemainingQuantity);

public sealed record KkdOpenOrderLine(
    string OrderNumber,
    long OrderLineId,
    int OrderLineSequence,
    long? StockId,
    string StockCode,
    string StockName,
    string? UnitCode,
    string? YapCode,
    string? ProjectCode,
    DateOnly? OrderDate,
    DateOnly? DeliveryDate,
    decimal RemainingQuantity,
    bool IsMapped,
    string? MappingMessage);

public interface IKkdDistributionService
{
    Task<KkdDistributionContext> GetContextAsync(long employeeId, bool includeOpenOrders = true, CancellationToken ct = default);
    Task<IReadOnlyList<KkdOpenOrderLine>> GetOpenOrderLinesAsync(long employeeId, string orderNumbersCsv, CancellationToken ct = default);
    Task<KkdDistributionCreateResult> CreateAsync(KkdDistributionCreateRequest request, long actor, CancellationToken ct = default);
    Task<IReadOnlyList<KkdDistributionRow>> GetRecentAsync(long actor, CancellationToken ct = default);
    Task<PagedResponse<KkdDistributionRow>> GetPagedAsync(PagedRequest request, long actor, CancellationToken ct = default);
    Task<KkdDistributionDetail> GetDetailAsync(long id, long actor, CancellationToken ct = default);
    Task<KkdDistributionCompleteResult> CompleteAsync(long id, KkdDistributionCompleteRequest request, long actor, CancellationToken ct = default);
    Task<KkdDistributionRow> DecideExcessApprovalAsync(long id, KkdExcessApprovalRequest request, long actor, CancellationToken ct = default);
    Task<KkdDistributionCompleteResult?> CompleteByWarehouseOutboundAsync(long warehouseOutboundId, Guid idempotencyKey, long actor, CancellationToken ct = default);
    Task CancelAsync(long id, Guid idempotencyKey, string reason, long actor, CancellationToken ct = default);
}
