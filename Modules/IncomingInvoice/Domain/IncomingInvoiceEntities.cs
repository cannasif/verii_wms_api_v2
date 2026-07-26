using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Domain;

public enum IncomingInvoiceKind
{
    EInvoice = 1,
    EArchive = 2
}

public enum IncomingInvoiceArchiveStatus
{
    Imported = 1,
    NeedsReview = 2,
    ReadyForReceipt = 3,
    PartiallyLinked = 4,
    Linked = 5,
    Rejected = 6
}

public enum IncomingInvoiceLineMatchStatus
{
    Unmatched = 1,
    StockMatched = 2,
    Ready = 3,
    Ignored = 4
}

public enum IncomingInvoiceDocumentFormat
{
    UblXml = 1,
    Pdf = 2
}

public enum IncomingInvoiceValidationStatus
{
    Parsed = 1,
    Warning = 2,
    Invalid = 3
}

public sealed class ELogoConnection : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Vkn { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? PasswordCipherText { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? EndpointUrl { get; set; }
    public string? ApplicationName { get; set; }
    public string? Version { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class IncomingInvoiceHeader : BaseEntity
{
    public long ELogoConnectionId { get; set; }
    public ELogoConnection ELogoConnection { get; set; } = null!;
    public string OwnerVkn { get; set; } = string.Empty;
    public Guid Uuid { get; set; }
    public IncomingInvoiceKind DocumentKind { get; set; }
    public string? ProfileId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public string InvoiceTypeCode { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public TimeOnly? IssueTime { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public string? OrderReferenceNo { get; set; }
    public string? DespatchReferenceNo { get; set; }

    public string SupplierVknOrTckn { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierTaxOffice { get; set; }
    public long? SupplierCustomerId { get; set; }
    public string CustomerVknOrTckn { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    public decimal LineExtensionAmount { get; set; }
    public decimal TaxExclusiveAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TaxInclusiveAmount { get; set; }
    public decimal AllowanceTotalAmount { get; set; }
    public decimal PayableAmount { get; set; }

    public IncomingInvoiceArchiveStatus ArchiveStatus { get; set; } = IncomingInvoiceArchiveStatus.Imported;
    public IncomingInvoiceValidationStatus ValidationStatus { get; set; } = IncomingInvoiceValidationStatus.Parsed;
    public string? ValidationMessage { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; set; }
    public DateTimeOffset LastSynchronizedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<IncomingInvoiceLine> Lines { get; set; } = [];
    public ICollection<IncomingInvoiceDocument> Documents { get; set; } = [];
    public ICollection<IncomingInvoiceGoodsReceiptLink> GoodsReceiptLinks { get; set; } = [];
}

public sealed class IncomingInvoiceLine : BaseEntity
{
    public long IncomingInvoiceId { get; set; }
    public IncomingInvoiceHeader Header { get; set; } = null!;
    public int LineNo { get; set; }
    public string ExternalLineId { get; set; } = string.Empty;
    public string StockCode { get; set; } = string.Empty;
    public string? BuyerStockCode { get; set; }
    public string StockName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal LineExtensionAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public long? StockId { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCode { get; set; }
    public IncomingInvoiceLineMatchStatus MatchStatus { get; set; } = IncomingInvoiceLineMatchStatus.Unmatched;
    public string? MatchMessage { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class IncomingInvoiceDocument : BaseEntity
{
    public long IncomingInvoiceId { get; set; }
    public IncomingInvoiceHeader Header { get; set; } = null!;
    public IncomingInvoiceDocumentFormat Format { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset StoredAtUtc { get; set; }
}

public sealed class IncomingInvoiceGoodsReceiptLink : BaseEntity
{
    public long IncomingInvoiceId { get; set; }
    public IncomingInvoiceHeader IncomingInvoice { get; set; } = null!;
    public long GoodsReceiptId { get; set; }
    public GoodsReceiptHeader GoodsReceipt { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public decimal LinkedQuantity { get; set; }
    public DateTimeOffset LinkedAtUtc { get; set; }
    public long LinkedBy { get; set; }
    public ICollection<IncomingInvoiceGoodsReceiptLineLink> Lines { get; set; } = [];
}

public sealed class IncomingInvoiceGoodsReceiptLineLink : BaseEntity
{
    public long IncomingInvoiceGoodsReceiptLinkId { get; set; }
    public IncomingInvoiceGoodsReceiptLink Link { get; set; } = null!;
    public long IncomingInvoiceLineId { get; set; }
    public IncomingInvoiceLine IncomingInvoiceLine { get; set; } = null!;
    public long GoodsReceiptLineId { get; set; }
    public GoodsReceiptLine GoodsReceiptLine { get; set; } = null!;
    public decimal LinkedQuantity { get; set; }
}
