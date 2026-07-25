using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.StockTracking.Domain;

public enum StockTrackingPolicyScope
{
    BranchDefault = 1,
    StockGroup = 2,
    Stock = 3
}

public enum SerialQuantityRule
{
    NotApplicable = 0,
    OneSerialPerLine = 1,
    OneSerialPerBaseUnit = 2
}

/// <summary>
/// WMS-owned, versioned tracking policy. ERP stock mirror data is never modified by this aggregate.
/// Resolution order is Stock, StockGroup, BranchDefault.
/// </summary>
public sealed class StockTrackingPolicy : BaseEntity
{
    public string PolicyCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public StockTrackingPolicyScope Scope { get; set; }
    public long? StockId { get; set; }
    public string? StockGroupCode { get; set; }
    public int Version { get; set; } = 1;
    public int Priority { get; set; } = 100;
    public StockTrackingType TrackingType { get; set; } = StockTrackingType.None;
    public bool RequireSerial { get; set; }
    public SerialQuantityRule SerialQuantityRule { get; set; }
    public bool AutoGenerateSerials { get; set; }
    public bool RequireLot { get; set; }
    public bool RequireManufacturingDate { get; set; }
    public bool RequireExpirationDate { get; set; }
    public int? MinimumRemainingShelfLifeDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
