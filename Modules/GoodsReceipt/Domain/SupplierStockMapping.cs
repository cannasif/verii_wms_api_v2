using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Domain;

/// <summary>
/// Maps a supplier-specific item identity from an external document to the WMS stock mirror.
/// This is the canonical source for incoming e-document and OCR line resolution.
/// </summary>
public sealed class SupplierStockMapping : BaseEntity
{
    public long SupplierId { get; set; }
    public string SupplierStockCode { get; set; } = string.Empty;
    public string NormalizedSupplierStockCode { get; set; } = string.Empty;
    public string? SupplierStockName { get; set; }
    public string? SupplierUnitCode { get; set; }
    public long StockId { get; set; }
    public decimal ConversionFactor { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
