using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Procurement.Application;

public sealed record ProcurementLineInput(long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,DateOnly? RequiredDate,string? ProjectCode,string? Description);
public sealed record CreateProcurementRequest(DateOnly? RequestDate,DateOnly? RequiredDate,string? DepartmentCode,string? ProjectCode,string Subject,string? Description,IReadOnlyList<ProcurementLineInput> Lines);
public sealed record ProcurementTransitionRequest(string? Note);
public sealed record ConvertRequestToRfqRequest(DateOnly ResponseDueDate,IReadOnlyList<long> SupplierIds,string? BuyerMessage);
public sealed record CreateSupplierQuoteRequest(long SupplierId,string QuoteNo,DateOnly? QuoteDate,DateOnly? ValidUntil,string CurrencyCode,decimal ExchangeRate,string? Note,IReadOnlyList<SupplierQuoteLineInput> Lines);
public sealed record SupplierQuoteLineInput(long RfqLineId,decimal Quantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? DeliveryDate);
public sealed record CreatePurchaseOrderRequest(long SupplierId,DateOnly? OrderDate,DateOnly? DeliveryDate,string CurrencyCode,decimal ExchangeRate,string? ProjectCode,string? Description,IReadOnlyList<PurchaseOrderLineInput> Lines);
public sealed record PurchaseOrderLineInput(long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? DeliveryDate,string? ProjectCode);

public sealed record ProcurementWorkspaceSummary(int DraftRequests,int PendingRequests,int OpenRfqs,int SubmittedQuotes,int PendingOrders,int ApprovedOpenOrders);
public sealed record ProcurementGridRow(long Id,string DocumentType,string DocumentNo,DateOnly DocumentDate,string Status,string Subject,string? Counterparty,int LineCount,decimal TotalAmount,string CurrencyCode,DateOnly? DueDate,DateTime? CreatedDate);
public sealed record ProcurementLineDetail(long Id,int LineNo,long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,decimal SecondaryQuantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? RequiredDate,string? ProjectCode);
public sealed record ProcurementSupplierParticipant(long SupplierId,string SupplierCode,string SupplierName);
public sealed record ProcurementDocumentDetail(long Id,string DocumentType,string DocumentNo,DateOnly DocumentDate,string Status,string Subject,string? Description,string? CounterpartyCode,string? CounterpartyName,string CurrencyCode,decimal ExchangeRate,DateOnly? DueDate,IReadOnlyList<ProcurementLineDetail> Lines,IReadOnlyList<ProcurementHistoryRow> History,IReadOnlyList<ProcurementSupplierParticipant>? Suppliers=null);
public sealed record ProcurementHistoryRow(string FromStatus,string ToStatus,long ActorUserId,string? Note,DateTimeOffset ChangedAtUtc);

// Bu sözleşme Procurement tablosunu GoodsReceipt tablolarına bağlamadan, onaylı siparişleri
// ileride IGoodsReceiptOrderSource adaptörüne sunar.
public sealed record ProcurementReceiptSourceLine(long PurchaseOrderId,long PurchaseOrderLineId,string OrderNo,int LineNo,long? StockId,string? StockCode,string StockName,string UnitCode,long SupplierId,string SupplierCode,string SupplierName,string? ProjectCode,DateOnly OrderDate,DateOnly? DeliveryDate,decimal OrderedQuantity,decimal ReceivedQuantity,decimal OpenQuantity);

public interface IProcurementService
{
    Task<ProcurementWorkspaceSummary> GetSummaryAsync(CancellationToken ct=default);
    Task<PagedResponse<ProcurementGridRow>> GetPagedAsync(string documentType,PagedRequest request,CancellationToken ct=default);
    Task<ProcurementDocumentDetail> GetDetailAsync(string documentType,long id,CancellationToken ct=default);
    Task<long> CreateRequestAsync(CreateProcurementRequest request,long actorUserId,CancellationToken ct=default);
    Task TransitionRequestAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default);
    Task<long> ConvertRequestToRfqAsync(long id,ConvertRequestToRfqRequest request,long actorUserId,CancellationToken ct=default);
    Task TransitionRfqAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default);
    Task<long> CreateQuoteAsync(long rfqId,CreateSupplierQuoteRequest request,long actorUserId,CancellationToken ct=default);
    Task TransitionQuoteAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default);
    Task<long> ConvertQuoteToOrderAsync(long id,long actorUserId,CancellationToken ct=default);
    Task<long> CreateOrderAsync(CreatePurchaseOrderRequest request,long actorUserId,CancellationToken ct=default);
    Task TransitionOrderAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default);
    Task<IReadOnlyList<ProcurementReceiptSourceLine>> GetOpenReceiptSourceLinesAsync(long? purchaseOrderId,CancellationToken ct=default);
}
