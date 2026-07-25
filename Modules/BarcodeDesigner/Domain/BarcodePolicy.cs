using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Domain;

public enum BarcodePolicyScope { ProductSerial = 1, ProductLot = 2, Location = 3, Logistics = 4, Document = 5 }
public enum BarcodePolicySegmentType { Field = 1, Literal = 2, Sequence = 3, Date = 4 }
public enum BarcodePolicyField { StockCode = 1, SerialNo = 2, YapCode = 3, LotNo = 4, WarehouseCode = 5, LocationCode = 6, DocumentNo = 7 }
public enum BarcodeValueTransform { None = 0, Upper = 1, Lower = 2 }

public sealed class BarcodePolicy : BaseEntity
{
    public string PolicyKey { get; set; } = "GLOBAL";
    public string DisplayName { get; set; } = "Genel Barkod Politikası";
    public int CurrentVersion { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class BarcodePolicyProfile : BaseEntity
{
    public long BarcodePolicyId { get; set; }
    public BarcodePolicyScope Scope { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Prefix { get; set; }
    public string Separator { get; set; } = "/";
    public long NextSequence { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class BarcodePolicyProfileSegment : BaseEntity
{
    public long BarcodePolicyProfileId { get; set; }
    public int Order { get; set; }
    public BarcodePolicySegmentType SegmentType { get; set; }
    public BarcodePolicyField? SourceField { get; set; }
    public string? LiteralValue { get; set; }
    public bool IsRequired { get; set; }
    public BarcodeValueTransform Transform { get; set; } = BarcodeValueTransform.Upper;
    public int SequenceLength { get; set; } = 8;
    public string DateFormat { get; set; } = "yyyyMMdd";
}

public sealed class GeneratedBarcode : BaseEntity
{
    public long BarcodePolicyId { get; set; }
    public long BarcodePolicyProfileId { get; set; }
    public int PolicyVersion { get; set; }
    public BarcodePolicyScope Scope { get; set; }
    public string BarcodeValue { get; set; } = string.Empty;
    public string BarcodeHash { get; set; } = string.Empty;
    public string IdempotencyHash { get; set; } = string.Empty;
    public string? StockCode { get; set; }
    public string? SerialNo { get; set; }
    public string? YapCode { get; set; }
    public string? LotNo { get; set; }
    public string? WarehouseCode { get; set; }
    public string? LocationCode { get; set; }
    public string? DocumentNo { get; set; }
    public long SequenceNo { get; set; }
    public DateTime GeneratedAt { get; set; }
}
