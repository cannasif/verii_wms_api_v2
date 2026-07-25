using verii_wms_api_v2.Shared.Domain;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.YapCode.Domain;

public sealed class YapCode : BaseEntity
{
    public string ConfigurationCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ConfigurableStockCode { get; set; }
    public long? StockId { get; set; }
    public StockEntity? Stock { get; set; }
    public DateTime? LastSyncDate { get; set; }
}
