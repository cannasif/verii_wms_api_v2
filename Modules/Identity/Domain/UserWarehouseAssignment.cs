using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Identity.Domain;

public sealed class UserWarehouseAssignment : BaseEntity
{
    public long UserId { get; set; }
    public long WarehouseId { get; set; }
}
