using System.Text.Json.Serialization;
using verii_wms_api_v2.Modules.InventoryCount.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.InventoryCount.Application;

public sealed record InventoryCountScopeRequest(
    long? LocationId,
    long? StockId,
    long? YapCodeId,
    string? StockGroupCode,
    bool IncludeDescendantLocations = true,
    bool IncludeEmptyLocations = false);

public sealed record CreateInventoryCountDraftRequest(
    string BranchCode,
    long WarehouseId,
    long? DocumentSeriesId,
    InventoryCountType CountType,
    InventoryCountMode? CountMode,
    InventoryCountMovementPolicy? MovementPolicy,
    int Priority,
    DateTime? PlannedStartUtc,
    DateTime? PlannedEndUtc,
    decimal? QuantityTolerance,
    decimal? PercentageTolerance,
    int? MaxCountAttempts,
    bool? RequireIndependentRecount,
    bool? AllowUnexpectedStock,
    bool? AutoApproveWithinTolerance,
    bool IncludeEmptyLocations,
    string? Description,
    IReadOnlyList<InventoryCountScopeRequest> Scopes);

public sealed record UpdateInventoryCountDraftRequest(
    InventoryCountType CountType,
    InventoryCountMode CountMode,
    InventoryCountMovementPolicy MovementPolicy,
    int Priority,
    DateTime? PlannedStartUtc,
    DateTime? PlannedEndUtc,
    decimal QuantityTolerance,
    decimal PercentageTolerance,
    int MaxCountAttempts,
    bool RequireIndependentRecount,
    bool AllowUnexpectedStock,
    bool AutoApproveWithinTolerance,
    bool IncludeEmptyLocations,
    string? Description,
    IReadOnlyList<InventoryCountScopeRequest> Scopes,
    string ConcurrencyToken);

public sealed record InventoryCountGridRow
{
    public long Id { get; init; }
    public Guid CountCode { get; init; }
    public string DocumentNo { get; init; } = string.Empty;
    public string BranchCode { get; init; } = string.Empty;
    public long WarehouseId { get; init; }
    public int WarehouseCode { get; init; }
    public string WarehouseName { get; init; } = string.Empty;
    public InventoryCountType CountType { get; init; }
    public InventoryCountMode CountMode { get; init; }
    public InventoryCountMovementPolicy MovementPolicy { get; init; }
    public InventoryCountStatus Status { get; init; }
    public int Priority { get; init; }
    public DateTime? PlannedStartUtc { get; init; }
    public DateTime? PlannedEndUtc { get; init; }
    public DateTime? SnapshotAtUtc { get; init; }
    public int TaskCount { get; init; }
    public int CompletedTaskCount { get; init; }
    public int LineCount { get; init; }
    public int CountedLineCount { get; init; }
    public int VarianceLineCount { get; init; }
    public string? Description { get; init; }
    public long? CreatedBy { get; init; }
    public DateTime? CreatedDate { get; init; }
    public long? UpdatedBy { get; init; }
    public DateTime? UpdatedDate { get; init; }
    public string ConcurrencyToken { get; init; } = string.Empty;
    [JsonIgnore] public string TaskProgressSearchText { get; init; } = string.Empty;
    [JsonIgnore] public string LineProgressSearchText { get; init; } = string.Empty;
    [JsonIgnore] public string? CreatedBySearchText { get; init; }
    [JsonIgnore] public string? UpdatedBySearchText { get; init; }
}

public sealed record InventoryCountScopeRow(
    long Id,
    int SequenceNo,
    long? LocationId,
    string? LocationCode,
    string? LocationName,
    long? StockId,
    string? StockCode,
    string? StockName,
    long? YapCodeId,
    string? YapCode,
    string? StockGroupCode,
    bool IncludeDescendantLocations,
    bool IncludeEmptyLocations);

public sealed record InventoryCountTaskRow(
    long Id,
    Guid TaskCode,
    string TaskNo,
    long LocationId,
    string LocationCode,
    string LocationName,
    int RouteSequence,
    int CountRound,
    InventoryCountTaskStatus Status,
    long? AssignedUserId,
    int LineCount,
    int CountedLineCount,
    int VarianceLineCount,
    bool LocationBarcodeConfirmed,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string ConcurrencyToken);

public sealed record InventoryCountLineRow(
    long Id,
    long TaskId,
    int SequenceNo,
    long LocationId,
    string LocationCode,
    long StockId,
    string StockCode,
    string StockName,
    long? YapCodeId,
    string? YapCode,
    string UnitCode,
    string? LotNo,
    string? SerialNo,
    string StockStatus,
    decimal? SnapshotQuantity,
    decimal CountedQuantity,
    decimal? VarianceQuantity,
    decimal? VariancePercentage,
    bool IsUnexpectedStock,
    bool IsWithinTolerance,
    InventoryCountLineStatus Status,
    string ConcurrencyToken);

public sealed record InventoryCountDetail(
    InventoryCountGridRow Header,
    decimal QuantityTolerance,
    decimal PercentageTolerance,
    int MaxCountAttempts,
    bool RequireIndependentRecount,
    bool AllowUnexpectedStock,
    bool AutoApproveWithinTolerance,
    bool IncludeEmptyLocations,
    long? SnapshotMovementEntryId,
    string ConcurrencyToken,
    IReadOnlyList<InventoryCountScopeRow> Scopes,
    IReadOnlyList<InventoryCountTaskRow> Tasks,
    IReadOnlyList<InventoryCountLineRow> Lines);

public sealed record InventoryCountPreviewResult(
    int LocationCount,
    int EmptyLocationCount,
    int BalanceLineCount,
    int DistinctStockCount,
    int DistinctLotCount,
    int DistinctSerialCount,
    decimal TotalQuantity,
    IReadOnlyList<string> Warnings);

public sealed record ReleaseInventoryCountRequest(string IdempotencyKey, string ConcurrencyToken);

public sealed record ReleaseInventoryCountResult(
    long HeaderId,
    string DocumentNo,
    int TaskCount,
    int LineCount,
    long SnapshotMovementEntryId,
    DateTime SnapshotAtUtc,
    bool IsReplay);

public sealed record InventoryCountPolicyResponse(
    long? Id,
    string BranchCode,
    long? WarehouseId,
    InventoryCountMode DefaultCountMode,
    InventoryCountMovementPolicy DefaultMovementPolicy,
    decimal QuantityTolerance,
    decimal PercentageTolerance,
    int MaxCountAttempts,
    bool RequireIndependentRecount,
    bool AllowUnexpectedStock,
    bool AutoApproveWithinTolerance,
    bool RequireDifferenceReason,
    bool IsActive,
    string? ConcurrencyToken);

public sealed record UpsertInventoryCountPolicyRequest(
    string BranchCode,
    long? WarehouseId,
    InventoryCountMode DefaultCountMode,
    InventoryCountMovementPolicy DefaultMovementPolicy,
    decimal QuantityTolerance,
    decimal PercentageTolerance,
    int MaxCountAttempts,
    bool RequireIndependentRecount,
    bool AllowUnexpectedStock,
    bool AutoApproveWithinTolerance,
    bool RequireDifferenceReason,
    bool IsActive,
    string? ConcurrencyToken);

public interface IInventoryCountService
{
    Task<PagedResponse<InventoryCountGridRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<InventoryCountDetail> GetDetailAsync(long id, bool revealBookQuantity, CancellationToken ct = default);
    Task<long> CreateDraftAsync(CreateInventoryCountDraftRequest request, long actor, CancellationToken ct = default);
    Task UpdateDraftAsync(long id, UpdateInventoryCountDraftRequest request, long actor, CancellationToken ct = default);
    Task DeleteDraftAsync(long id, long actor, CancellationToken ct = default);
    Task<InventoryCountPreviewResult> PreviewAsync(long id, CancellationToken ct = default);
    Task<ReleaseInventoryCountResult> ReleaseAsync(long id, ReleaseInventoryCountRequest request, long actor, CancellationToken ct = default);
    Task<InventoryCountPolicyResponse> GetPolicyAsync(string branchCode, long? warehouseId, CancellationToken ct = default);
    Task<InventoryCountPolicyResponse> UpsertPolicyAsync(UpsertInventoryCountPolicyRequest request, long actor, CancellationToken ct = default);
}
