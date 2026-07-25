using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Location.Domain;

public sealed class WarehouseLocation : BaseEntity
{
    public long WarehouseId { get; set; }
    public long? ParentLocationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LocationType { get; set; } = LocationTypes.Cell;
    public string BarcodeEntryMode { get; set; } = BarcodeEntryModes.Auto;
    public string? Barcode { get; set; }
    public string? ZoneCode { get; set; }
    public int? AisleNo { get; set; }
    public int? RackNo { get; set; }
    public int? LevelNo { get; set; }
    public int? BinNo { get; set; }
    public decimal? CapacityQuantity { get; set; }
    public decimal? CapacityWeight { get; set; }
    public decimal? CapacityVolume { get; set; }
    public string? CapacityUnit { get; set; }
    public bool AllowMixedStock { get; set; }
    public bool AllowMixedLot { get; set; }
    public bool AllowMixedStatus { get; set; }
    public bool AllowCycleCount { get; set; } = true;
    public bool IsPickable { get; set; } = true;
    public bool IsPutaway { get; set; } = true;
    public bool IsQuarantine { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class LocationTypes
{
    public const string Zone = "Zone";
    public const string Aisle = "Aisle";
    public const string Rack = "Rack";
    public const string Shelf = "Shelf";
    public const string Cell = "Cell";
    public const string Receiving = "Receiving";
    public const string Staging = "Staging";
    public const string Shipping = "Shipping";
    public const string Quarantine = "Quarantine";
    public const string Virtual = "Virtual";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { Zone, Aisle, Rack, Shelf, Cell, Receiving, Staging, Shipping, Quarantine, Virtual };
}

public static class BarcodeEntryModes
{
    public const string Auto = "Auto";
    public const string Manual = "Manual";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Auto, Manual };
}
