using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record SaveSupplierStockMappingRequest(
    string BranchCode,
    long SupplierId,
    string SupplierStockCode,
    string? SupplierStockName,
    string? SupplierUnitCode,
    long StockId,
    decimal ConversionFactor,
    bool IsActive,
    string? Notes,
    byte[]? RowVersion);

public sealed class SupplierStockMappingRow
{
    public long Id { get; init; }
    public string BranchCode { get; init; } = string.Empty;
    public long SupplierId { get; init; }
    public string SupplierCode { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string SupplierStockCode { get; init; } = string.Empty;
    public string? SupplierStockName { get; init; }
    public string? SupplierUnitCode { get; init; }
    public long StockId { get; init; }
    public string SystemStockCode { get; init; } = string.Empty;
    public string SystemStockName { get; init; } = string.Empty;
    public string SystemUnitCode { get; init; } = string.Empty;
    public decimal ConversionFactor { get; init; }
    public bool IsActive { get; init; }
    public string? Notes { get; init; }
    public long? CreatedBy { get; init; }
    public DateTime? CreatedDate { get; init; }
    public long? UpdatedBy { get; init; }
    public DateTime? UpdatedDate { get; init; }
    public byte[] RowVersion { get; init; } = [];
}

public sealed record SupplierStockResolution(
    long MappingId,
    long SupplierId,
    long StockId,
    string SystemStockCode,
    string SystemStockName,
    string SystemUnitCode,
    decimal ConversionFactor);

public interface ISupplierStockMappingService
{
    Task<PagedResponse<SupplierStockMappingRow>> GetPagedAsync(
        string branchCode, PagedRequest request, CancellationToken ct = default);
    Task<SupplierStockMappingRow> GetAsync(
        long id, string branchCode, CancellationToken ct = default);
    Task<SupplierStockMappingRow> CreateAsync(
        SaveSupplierStockMappingRequest request, CancellationToken ct = default);
    Task<SupplierStockMappingRow> UpdateAsync(
        long id, SaveSupplierStockMappingRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, string branchCode, CancellationToken ct = default);
    Task<SupplierStockResolution?> ResolveAsync(
        string branchCode, long supplierId, string supplierStockCode,
        CancellationToken ct = default);
}
