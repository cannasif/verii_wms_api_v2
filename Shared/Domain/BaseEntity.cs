namespace verii_wms_api_v2.Shared.Domain;

public abstract class BaseEntity
{
    public long Id { get; set; }
    public string BranchCode { get; set; } = "0";
    public DateTime? CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedDate { get; set; }
    public DateTime? DeletedDate { get; set; }
    public bool IsDeleted { get; set; } = false;
    public long? CreatedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public long? DeletedBy { get; set; }
}
