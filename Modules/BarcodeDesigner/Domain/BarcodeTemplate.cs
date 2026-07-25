using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Domain;

public enum BarcodeLabelType { Product = 1, SerialLot = 2, Location = 3, Logistics = 4, Sscc = 5 }

public sealed class BarcodeTemplate : BaseEntity
{
    public string TemplateCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public BarcodeLabelType LabelType { get; set; } = BarcodeLabelType.Product;
    public decimal WidthMm { get; set; } = 100;
    public decimal HeightMm { get; set; } = 70;
    public int Dpi { get; set; } = 203;
    public string EngineType { get; set; } = "konva+bwip";
    public bool IsActive { get; set; } = true;
    public long? DraftVersionId { get; set; }
    public long? PublishedVersionId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class BarcodeTemplateVersion : BaseEntity
{
    public long BarcodeTemplateId { get; set; }
    public int VersionNo { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? Notes { get; set; }
    public string TemplateJson { get; set; } = "{}";
    public byte[] RowVersion { get; set; } = [];
}
