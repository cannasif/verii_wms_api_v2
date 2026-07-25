using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.DocumentSeries.Domain;

public enum WmsDocumentType
{
    GoodsReceipt = 1,
    InterWarehouseTransfer = 2,
    Shipment = 3,
    WarehouseReceipt = 4,
    WarehouseIssue = 5
}

public enum DocumentYearFormat
{
    None = 0,
    TwoDigit = 2,
    FourDigit = 4
}

public sealed class DocumentSeries : BaseEntity
{
    public long? WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public WmsDocumentType DocumentType { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public string Separator { get; set; } = "-";
    public DocumentYearFormat YearFormat { get; set; } = DocumentYearFormat.FourDigit;
    public int NumberLength { get; set; } = 8;
    public long StartNumber { get; set; } = 1;
    public long NextNumber { get; set; } = 1;
    public int IncrementBy { get; set; } = 1;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public bool HasIssuedNumbers { get; set; }
    public DateTime? LastIssuedAt { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
