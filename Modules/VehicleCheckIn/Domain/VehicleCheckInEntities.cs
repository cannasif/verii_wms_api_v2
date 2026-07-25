using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.VehicleCheckIn.Domain;

public enum VehicleCheckInStatus { CheckedIn=1, LinkedToReceipt=2, Completed=3, Cancelled=4 }

public sealed class VehicleCheckInHeader : BaseEntity
{
    public string PlateNo { get; set; } = string.Empty;
    public string PlateNoNormalized { get; set; } = string.Empty;
    public string? TrailerPlateNo { get; set; }
    public string? TrailerPlateNoNormalized { get; set; }
    public string? DriverFirstName { get; set; }
    public string? DriverLastName { get; set; }
    public string? DriverPhone { get; set; }
    public string? CarrierName { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerCodeSnapshot { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public DateTimeOffset CheckedInAtUtc { get; set; }
    public DateOnly BusinessDate { get; set; }
    public VehicleCheckInStatus Status { get; set; } = VehicleCheckInStatus.CheckedIn;
    public string? Note { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<VehicleCheckInImage> Images { get; set; } = [];
}

public sealed class VehicleCheckInImage : BaseEntity
{
    public long HeaderId { get; set; }
    public VehicleCheckInHeader Header { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int SortOrder { get; set; }
}
