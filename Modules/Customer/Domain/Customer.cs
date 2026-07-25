using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Customer.Domain;

public sealed class Customer : BaseEntity
{
    public short BusinessUnitCode { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime? LastSyncDate { get; set; }
}
