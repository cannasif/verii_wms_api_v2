using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Stock.Domain;

public sealed class Stock : BaseEntity
{
    public ICollection<StockImage> Images { get; set; } = [];
    public short BusinessUnitCode { get; set; }
    public string ErpStockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = "ADET";
    public string? ManufacturerCode { get; set; }
    public string? GroupCode { get; set; }
    public string? Code1 { get; set; }
    public string? Code2 { get; set; }
    public string? Code3 { get; set; }
    public string? Code4 { get; set; }
    public string? Code5 { get; set; }
    public DateTime? LastSyncDate { get; set; }
}
