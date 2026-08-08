using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdRequestLineCreateRequest(
    string GroupCode,
    string? GroupName,
    long? StockId,
    decimal Quantity,
    string? ExternalOrderNo = null,
    string? ExternalOrderLineId = null);

public sealed record KkdRequestCreateRequest(
    Guid IdempotencyKey,
    long EmployeeId,
    long? WarehouseId,
    long? AssignedUserId,
    KkdRequestSourceType SourceType,
    string? ExternalRequestNo,
    KkdRequestPriority Priority,
    DateTimeOffset? NeededAtUtc,
    string? Description,
    IReadOnlyList<KkdRequestLineCreateRequest> Lines);

public sealed record KkdRequestResolveLineRequest(
    Guid IdempotencyKey,
    long StockId,
    string Reason,
    string? ExpectedRowVersion);

public sealed record KkdRequestAssignRequest(
    long? WarehouseId,
    long? AssignedUserId,
    string? ExpectedRowVersion);

public sealed record KkdRequestCancelRequest(
    Guid IdempotencyKey,
    string Reason,
    string? ExpectedRowVersion);

public sealed record KkdRequestGridRow(
    long Id,
    string RequestNo,
    string Status,
    string Priority,
    string SourceType,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string DepartmentName,
    string RoleName,
    long? WarehouseId,
    long? AssignedUserId,
    string? ExternalRequestNo,
    int TotalLineCount,
    int UnresolvedLineCount,
    decimal RequestedQuantity,
    decimal AllocatedQuantity,
    decimal DeliveredQuantity,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? NeededAtUtc,
    long? CreatedBy,
    DateTime? CreatedDate,
    long? UpdatedBy,
    DateTime? UpdatedDate);

public sealed record KkdRequestLineResolutionRow(
    long Id,
    long? PreviousStockId,
    long StockId,
    string StockCode,
    string? StockName,
    string Reason,
    long? ResolvedBy,
    DateTimeOffset ResolvedAtUtc);

public sealed record KkdRequestLineDetail(
    long Id,
    int LineNo,
    string GroupCode,
    string? GroupName,
    long? StockId,
    string? StockCode,
    string? StockName,
    string UnitCode,
    decimal RequestedQuantity,
    decimal AllocatedQuantity,
    decimal DeliveredQuantity,
    decimal RemainingQuantity,
    string Status,
    string? ExternalOrderNo,
    string? ExternalOrderLineId,
    long? ResolvedByUserId,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionReason,
    string RowVersion,
    IReadOnlyList<KkdRequestLineResolutionRow> Resolutions);

public sealed record KkdRequestDetail(
    long Id,
    Guid CorrelationId,
    string RequestNo,
    string Status,
    string Priority,
    string SourceType,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string DepartmentName,
    string RoleName,
    long CustomerId,
    long? WarehouseId,
    long? AssignedUserId,
    string? ExternalRequestNo,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? NeededAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    string? Description,
    string RowVersion,
    IReadOnlyList<KkdRequestLineDetail> Lines);

public interface IKkdRequestService
{
    Task<PagedResponse<KkdRequestGridRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<KkdRequestDetail> GetDetailAsync(long id, CancellationToken ct = default);
    Task<KkdRequestDetail> CreateAsync(KkdRequestCreateRequest request, long actor, CancellationToken ct = default);
    Task<KkdRequestDetail> ResolveLineAsync(long requestId, long lineId, KkdRequestResolveLineRequest request, long actor, CancellationToken ct = default);
    Task<KkdRequestDetail> AssignAsync(long id, KkdRequestAssignRequest request, long actor, CancellationToken ct = default);
    Task<KkdRequestDetail> CancelAsync(long id, KkdRequestCancelRequest request, long actor, CancellationToken ct = default);
}
