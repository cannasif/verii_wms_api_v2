using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Customer.Domain;

public sealed class Customer : BaseEntity
{
    public short BusinessUnitCode { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Phone3 { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? CountryCode { get; set; }
    public string? Address { get; set; }
    public string? CustomerType { get; set; }
    public string? TaxOffice { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public DateTime? LastSyncDate { get; set; }
}
