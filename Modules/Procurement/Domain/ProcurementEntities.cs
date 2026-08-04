using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Procurement.Domain;

public enum ProcurementRequestStatus { Draft=1, PendingApproval=2, Approved=3, Rejected=4, Converted=5, Cancelled=6 }
public enum ProcurementRfqStatus { Draft=1, Sent=2, Quoted=3, Closed=4, Cancelled=5 }
public enum ProcurementQuoteStatus { Draft=1, Submitted=2, Approved=3, Rejected=4, Converted=5, Cancelled=6 }
public enum ProcurementOrderStatus { Draft=1, PendingApproval=2, Approved=3, SentToSupplier=4, PartiallyReceived=5, Received=6, Cancelled=7 }

public sealed class ProcurementRequest : BaseEntity
{
    public string RequestNo { get; set; } = string.Empty;
    public DateOnly RequestDate { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public string? DepartmentCode { get; set; }
    public string? ProjectCode { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ProcurementRequestStatus Status { get; set; } = ProcurementRequestStatus.Draft;
    public DateTimeOffset? SubmittedAtUtc { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public long? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ProcurementRequestLine> Lines { get; set; } = [];
}

public sealed class ProcurementRequestLine : BaseEntity
{
    public long ProcurementRequestId { get; set; }
    public ProcurementRequest Request { get; set; } = null!;
    public int LineNo { get; set; }
    public long? StockId { get; set; }
    public string? StockCodeSnapshot { get; set; }
    public string StockNameSnapshot { get; set; } = string.Empty;
    public string UnitCode { get; set; } = "ADET";
    public decimal RequestedQuantity { get; set; }
    public decimal ConvertedQuantity { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public string? ProjectCode { get; set; }
    public string? Description { get; set; }
}

public sealed class ProcurementRfq : BaseEntity
{
    public string RfqNo { get; set; } = string.Empty;
    public DateOnly RfqDate { get; set; }
    public DateOnly ResponseDueDate { get; set; }
    public long? ProcurementRequestId { get; set; }
    public ProcurementRequest? Request { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? BuyerMessage { get; set; }
    public ProcurementRfqStatus Status { get; set; } = ProcurementRfqStatus.Draft;
    public DateTimeOffset? SentAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ProcurementRfqLine> Lines { get; set; } = [];
    public ICollection<ProcurementRfqSupplier> Suppliers { get; set; } = [];
    public ICollection<ProcurementSupplierQuote> Quotes { get; set; } = [];
}

public sealed class ProcurementRfqLine : BaseEntity
{
    public long ProcurementRfqId { get; set; }
    public ProcurementRfq Rfq { get; set; } = null!;
    public long? ProcurementRequestLineId { get; set; }
    public int LineNo { get; set; }
    public long? StockId { get; set; }
    public string? StockCodeSnapshot { get; set; }
    public string StockNameSnapshot { get; set; } = string.Empty;
    public string UnitCode { get; set; } = "ADET";
    public decimal RequestedQuantity { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public string? ProjectCode { get; set; }
}

public sealed class ProcurementRfqSupplier : BaseEntity
{
    public long ProcurementRfqId { get; set; }
    public ProcurementRfq Rfq { get; set; } = null!;
    public long SupplierId { get; set; }
    public string SupplierCodeSnapshot { get; set; } = string.Empty;
    public string SupplierNameSnapshot { get; set; } = string.Empty;
}

public sealed class ProcurementSupplierQuote : BaseEntity
{
    public long ProcurementRfqId { get; set; }
    public ProcurementRfq Rfq { get; set; } = null!;
    public long SupplierId { get; set; }
    public string SupplierCodeSnapshot { get; set; } = string.Empty;
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string QuoteNo { get; set; } = string.Empty;
    public DateOnly QuoteDate { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1;
    public ProcurementQuoteStatus Status { get; set; } = ProcurementQuoteStatus.Submitted;
    public string? Note { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ProcurementSupplierQuoteLine> Lines { get; set; } = [];
}

public sealed class ProcurementSupplierQuoteLine : BaseEntity
{
    public long ProcurementSupplierQuoteId { get; set; }
    public ProcurementSupplierQuote Quote { get; set; } = null!;
    public long ProcurementRfqLineId { get; set; }
    public int LineNo { get; set; }
    public decimal QuotedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal VatRate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
}

public sealed class ProcurementPurchaseOrder : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public long SupplierId { get; set; }
    public string SupplierCodeSnapshot { get; set; } = string.Empty;
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public long? SourceQuoteId { get; set; }
    public string CurrencyCode { get; set; } = "TRY";
    public decimal ExchangeRate { get; set; } = 1;
    public string? ProjectCode { get; set; }
    public string? Description { get; set; }
    public ProcurementOrderStatus Status { get; set; } = ProcurementOrderStatus.Draft;
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public long? ApprovedBy { get; set; }
    public string? ErpOrderNo { get; set; }
    public DateTimeOffset? ErpPostedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ProcurementPurchaseOrderLine> Lines { get; set; } = [];
}

public sealed class ProcurementPurchaseOrderLine : BaseEntity
{
    public long ProcurementPurchaseOrderId { get; set; }
    public ProcurementPurchaseOrder Order { get; set; } = null!;
    public long? SourceQuoteLineId { get; set; }
    public int LineNo { get; set; }
    public long? StockId { get; set; }
    public string? StockCodeSnapshot { get; set; }
    public string StockNameSnapshot { get; set; } = string.Empty;
    public string UnitCode { get; set; } = "ADET";
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal VatRate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public string? ProjectCode { get; set; }
}

public sealed class ProcurementStatusHistory : BaseEntity
{
    public string DocumentType { get; set; } = string.Empty;
    public long DocumentId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public long ActorUserId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
}
