using verii_wms_api_v2.Modules.StockMovement.Domain;
using System.Text.Json.Serialization;
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
    decimal QuantityDelta, DateTime OccurredAt, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate,
    [property: JsonIgnore] string? ReferenceSearchText=null,
    [property: JsonIgnore] string? QuantitySearchText=null);

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
    decimal TotalQuantity, IReadOnlyList<OpeningBalanceImportRowResult> Rows,
    int BatchCount = 1, bool RowsTruncated = false);
public sealed record OpeningBalanceImportValidation(int TotalRows, decimal TotalQuantity, int BatchCount);
public sealed record WarehouseOpeningBalanceState(
    string SnapshotHash,
    int ExistingMovementCount,
    int CurrentBalanceRowCount,
    decimal CurrentTotalQuantity,
    int ReservedBalanceRowCount,
    decimal ReservedQuantity);

public sealed record StockReservationLineRequest(long ReferenceLineId, long WarehouseId, long LocationId, long StockId,
    long? YapCodeId, string UnitCode, string? LotNo, string? SerialNo, string StockStatus, decimal QuantityDelta);
public sealed record PostStockReservationRequest(string IdempotencyKey, string ReferenceType, long ReferenceId,
    string? ReferenceNo, string OperationType, string? Reason, IReadOnlyList<StockReservationLineRequest> Lines);
public sealed record StockReservationPostResult(long OperationId, bool Replayed, decimal QuantityDelta);

public sealed record ResolveSerialLocationsRequest(string BranchCode, long WarehouseId, long StockId,
    long? YapCodeId, IReadOnlyList<string> SerialNumbers);
public sealed record SerialLocationMatchDto(string SerialNo, long? LocationId, string? LocationCode,
    string? LocationName, decimal AvailableQuantity);
public sealed record StockLocationBalanceDto(long LocationId, string LocationCode, string LocationName,
    decimal AvailableQuantity, decimal Quantity = 0, decimal ReservedQuantity = 0);

public sealed record WarehouseInventoryLookup(
    long WarehouseId,
    int WarehouseCode,
    string WarehouseName,
    string BranchCode,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int DistinctStockCount,
    int DistinctLocationCount,
    bool LinesTruncated,
    IReadOnlyList<LocationBalanceRow> Lines);

public sealed record LocationInventoryLookup(
    long LocationId,
    string LocationCode,
    string LocationName,
    string LocationType,
    long WarehouseId,
    int WarehouseCode,
    string WarehouseName,
    string BranchCode,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int DistinctStockCount,
    bool LinesTruncated,
    IReadOnlyList<LocationBalanceRow> Lines);

public sealed record SerialInventoryLookup(
    SerialBalanceRow Balance,
    IReadOnlyList<SerialMovementHistoryRow> RecentMovements);

public sealed record LotInventoryLookup(
    string LotNo,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int DistinctStockCount,
    int DistinctLocationCount,
    bool LinesTruncated,
    IReadOnlyList<LocationBalanceRow> Lines);

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
    Task<IReadOnlyList<SerialLocationMatchDto>> ResolveSerialLocationsAsync(ResolveSerialLocationsRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockLocationBalanceDto>> ResolveStockLocationsAsync(string branchCode, long warehouseId, long stockId, long? yapCodeId, IReadOnlyCollection<long>? excludeLocationIds = null, bool includeOnHand = false, CancellationToken cancellationToken = default);
    Task<WarehouseInventoryLookup> GetWarehouseInventoryLookupAsync(long warehouseId, CancellationToken cancellationToken = default);
    Task<LocationInventoryLookup> GetLocationInventoryLookupAsync(long locationId, CancellationToken cancellationToken = default);
    Task<SerialInventoryLookup> GetSerialInventoryLookupAsync(long serialBalanceId, CancellationToken cancellationToken = default);
    Task<LotInventoryLookup> GetLotInventoryLookupAsync(string? lotNo, CancellationToken cancellationToken = default);
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
        bool replaceExistingBalances = false,
        string? expectedBalanceSnapshotHash = null,
        CancellationToken cancellationToken = default);
    Task<OpeningBalanceImportValidation> ValidateWarehouseOpeningAsync(
        Stream workbookStream,
        string branchCode,
        bool replaceExistingBalances = false,
        string? expectedBalanceSnapshotHash = null,
        CancellationToken cancellationToken = default);
    Task<WarehouseOpeningBalanceState> AnalyzeWarehouseStateAsync(
        IReadOnlyCollection<long> warehouseIds,
        CancellationToken cancellationToken = default);
}
