using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Warehouse.Domain;

public sealed class Warehouse : BaseEntity
{
    public int WarehouseCode { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public long? DefaultGoodsReceiptLocationId { get; set; }
    public DateTime? LastSyncDate { get; set; }
}
