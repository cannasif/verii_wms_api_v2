using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.StockBalance.Application;

public sealed record LocationBalanceRow(long Id, string BranchCode, long WarehouseId, int WarehouseCode, string WarehouseName,
    long LocationId, string LocationCode, string LocationName, long StockId, string StockCode, string StockName,
    long? YapCodeId, string? YapCode, string UnitCode, string? LotNo, string? SerialNo, string StockStatus,
    decimal Quantity, decimal ReservedQuantity, decimal AvailableQuantity, long LastMovementEntryId, DateTime LastTransactionDate,
    long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);

public sealed record WarehouseBalanceRow(long Id, string BranchCode, long WarehouseId, int WarehouseCode, string WarehouseName,
    long StockId, string StockCode, string StockName, long? YapCodeId, string? YapCode, string UnitCode, string StockStatus,
    decimal Quantity, decimal ReservedQuantity, decimal AvailableQuantity, int DistinctLocationCount, int DistinctLotCount,
    int DistinctSerialCount, long LastMovementEntryId, DateTime LastTransactionDate,
    long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);

public sealed record SerialBalanceRow(long Id, string BranchCode, long WarehouseId, int WarehouseCode, string WarehouseName,
    long LocationId, string LocationCode, string LocationName, long StockId, string StockCode, string StockName,
    long? YapCodeId, string? YapCode, string UnitCode, string? LotNo, string SerialNo, string StockStatus,
    decimal Quantity, decimal ReservedQuantity, decimal AvailableQuantity, long LastMovementEntryId, DateTime LastTransactionDate,
    long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);

public sealed record SerialMovementHistoryRow(long Id, long OperationId, Guid OperationCode, string OperationType, string OperationStatus,
    string? ReferenceType, string? ReferenceNo, long WarehouseId, int WarehouseCode, string WarehouseName,
    long LocationId, string LocationCode, string LocationName, long StockId, string StockCode, string StockName,
    long? YapCodeId, string? YapCode, string UnitCode, string? LotNo, string SerialNo, string StockStatus,
    decimal QuantityDelta, DateTime OccurredAt, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);

public sealed record StockBalanceDrillDown(WarehouseBalanceRow Summary, IReadOnlyList<LocationBalanceRow> Locations);
public sealed record ProjectionRebuildResult(int LocationRows, int WarehouseRows, long LastMovementEntryId, DateTime RebuiltAt);
public sealed record ReconciliationSummary(int LedgerGroupCount, int ProjectionGroupCount, int MismatchCount, int MissingProjectionCount,
    int ExtraProjectionCount, long LedgerLastEntryId, long ProjectionLastEntryId, DateTime CheckedAt);
public sealed record ReconciliationIssue(string IssueType, long WarehouseId, long LocationId, long StockId, long? YapCodeId,
    string UnitCode, string? LotNo, string? SerialNo, string StockStatus, decimal LedgerQuantity, decimal ProjectionQuantity,
    decimal Difference, long LedgerLastEntryId, long ProjectionLastEntryId);
public sealed record OpeningBalanceImportRowResult(int RowNumber, string Status, string WarehouseCode, string LocationCode,
    string StockCode, string Message);
public sealed record OpeningBalanceImportResult(long OperationId, Guid OperationCode, bool IsReplay, int TotalRows,
    decimal TotalQuantity, IReadOnlyList<OpeningBalanceImportRowResult> Rows);

public sealed record StockReservationLineRequest(long ReferenceLineId, long WarehouseId, long LocationId, long StockId,
    long? YapCodeId, string UnitCode, string? LotNo, string? SerialNo, string StockStatus, decimal QuantityDelta);
public sealed record PostStockReservationRequest(string IdempotencyKey, string ReferenceType, long ReferenceId,
    string? ReferenceNo, string OperationType, string? Reason, IReadOnlyList<StockReservationLineRequest> Lines);
public sealed record StockReservationPostResult(long OperationId, bool Replayed, decimal QuantityDelta);

public interface IStockBalanceService
{
    Task ApplyEntriesAsync(IReadOnlyCollection<StockMovementEntry> entries, CancellationToken cancellationToken = default);
    Task<StockReservationPostResult> PostReservationAsync(PostStockReservationRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<LocationBalanceRow>> GetLocationBalancesAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<WarehouseBalanceRow>> GetWarehouseBalancesAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<SerialBalanceRow>> GetSerialBalancesAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<SerialMovementHistoryRow>> GetSerialMovementHistoryAsync(long serialBalanceId, PagedRequest request, CancellationToken cancellationToken = default);
    Task<StockBalanceDrillDown> GetDrillDownAsync(long warehouseBalanceId, CancellationToken cancellationToken = default);
    Task<ProjectionRebuildResult> RebuildAsync(CancellationToken cancellationToken = default);
    Task<ReconciliationSummary> GetReconciliationSummaryAsync(CancellationToken cancellationToken = default);
    Task<PagedResponse<ReconciliationIssue>> GetReconciliationIssuesAsync(PagedRequest request, CancellationToken cancellationToken = default);
}

public interface IStockBalanceJobRunner
{
    Task ReconcileAndRepairAsync(CancellationToken cancellationToken = default);
}

public interface IOpeningBalanceImportService
{
    Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken cancellationToken = default);
    Task<OpeningBalanceImportResult> ImportAsync(Stream workbookStream, string branchCode, string idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<OpeningBalanceImportResult> ImportWarehouseOpeningAsync(
        Stream workbookStream,
        string branchCode,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
