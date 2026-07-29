using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Location.Application;

public sealed record LocationUpsertRequest(
    long WarehouseId,
    long? ParentLocationId,
    string Code,
    string Name,
    string LocationType,
    string BarcodeEntryMode,
    string? Barcode,
    string? ZoneCode,
    int? AisleNo,
    int? RackNo,
    int? LevelNo,
    int? BinNo,
    decimal? CapacityQuantity,
    decimal? CapacityWeight,
    decimal? CapacityVolume,
    string? CapacityUnit,
    bool AllowMixedStock,
    bool AllowMixedLot,
    bool AllowMixedStatus,
    bool AllowCycleCount,
    bool IsPickable,
    bool IsPutaway,
    bool IsQuarantine,
    bool IsActive,
    string? Description);

public sealed class LocationGridRow
{
    public long Id { get; init; }
    public string BranchCode { get; init; } = string.Empty;
    public long WarehouseId { get; init; }
    public int WarehouseCode { get; init; }
    public string WarehouseName { get; init; } = string.Empty;
    public long? ParentLocationId { get; init; }
    public string? ParentCode { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string LocationType { get; init; } = string.Empty;
    public string BarcodeEntryMode { get; init; } = string.Empty;
    public string? Barcode { get; init; }
    public string? ZoneCode { get; init; }
    public int? AisleNo { get; init; }
    public int? RackNo { get; init; }
    public int? LevelNo { get; init; }
    public int? BinNo { get; init; }
    public decimal? CapacityQuantity { get; init; }
    public decimal? CapacityWeight { get; init; }
    public decimal? CapacityVolume { get; init; }
    public string? CapacityUnit { get; init; }
    public bool AllowMixedStock { get; init; }
    public bool AllowMixedLot { get; init; }
    public bool AllowMixedStatus { get; init; }
    public bool AllowCycleCount { get; init; }
    public bool IsPickable { get; init; }
    public bool IsPutaway { get; init; }
    public bool IsQuarantine { get; init; }
    public bool IsActive { get; init; }
    public string? Description { get; init; }
    public long? CreatedBy { get; init; }
    public DateTime? CreatedDate { get; init; }
    public long? UpdatedBy { get; init; }
    public DateTime? UpdatedDate { get; init; }
}

public sealed record LocationLookupRow(long Id, long WarehouseId, long? ParentLocationId, string Code, string Name, string LocationType, string? Barcode);
public sealed record LocationStats(int Total, int Active, int Pickable, int Quarantine);
public sealed record PutawayLocationSuggestion(
    long Id,
    long WarehouseId,
    string Code,
    string Name,
    string LocationType,
    string? ZoneCode,
    decimal CurrentStockQuantity,
    decimal CurrentAvailableQuantity,
    decimal TotalLocationQuantity,
    decimal? CapacityQuantity,
    decimal? RemainingCapacity,
    bool ContainsStock,
    bool IsEmpty,
    int Score,
    string Reason);

public sealed record LocationImportRowResult(int RowNumber, string Status, string WarehouseCode, string LocationCode, string Message);
public sealed record LocationImportResult(int TotalRows, int CreatedRows, int FailedRows, IReadOnlyList<LocationImportRowResult> Rows);

public interface ILocationService
{
    Task<PagedResponse<LocationGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<LocationGridRow> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocationLookupRow>> GetLookupAsync(long warehouseId, bool includeInactive, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PutawayLocationSuggestion>> GetPutawaySuggestionsAsync(
        long warehouseId,
        long? stockId,
        string? stockCode,
        long? yapCodeId,
        decimal quantity,
        int limit,
        CancellationToken cancellationToken = default);
    Task<LocationStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<long> CreateAsync(LocationUpsertRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(long id, LocationUpsertRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public interface ILocationImportService
{
    Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken cancellationToken = default);
    Task<LocationImportResult> ImportAsync(Stream workbookStream, string branchCode, CancellationToken cancellationToken = default);
}
