using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Procurement.Application;

public sealed record ProcurementLineInput(long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,DateOnly? RequiredDate,string? ProjectCode,string? Description);
public sealed record CreateProcurementRequest(DateOnly? RequestDate,DateOnly? RequiredDate,string? DepartmentCode,string? ProjectCode,string Subject,string? Description,IReadOnlyList<ProcurementLineInput> Lines,string? RequestNo=null);
public sealed record ProcurementTransitionRequest(string? Note,IReadOnlyList<long>? RequestLineIds=null);
public sealed record RfqRequestLineInput(long RequestLineId,decimal Quantity);
public sealed record ConvertRequestToRfqRequest(DateOnly ResponseDueDate,IReadOnlyList<long>? SupplierIds,string? BuyerMessage,IReadOnlyList<RfqRequestLineInput>? Lines=null,string? RfqNo=null);
public sealed record CreateSupplierQuoteRequest(long? SupplierId,string QuoteNo,DateOnly? QuoteDate,DateOnly? ValidUntil,string CurrencyCode,decimal ExchangeRate,string? Note,IReadOnlyList<SupplierQuoteLineInput> Lines,string? SupplierName=null);
public sealed record SupplierQuoteLineInput(long RfqLineId,decimal Quantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? DeliveryDate);
public sealed record CreatePurchaseOrderRequest(long SupplierId,DateOnly? OrderDate,DateOnly? DeliveryDate,string CurrencyCode,decimal ExchangeRate,string? ProjectCode,string? Description,IReadOnlyList<PurchaseOrderLineInput> Lines,string? OrderNo=null);
public sealed record PurchaseOrderLineInput(long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? DeliveryDate,string? ProjectCode);
public sealed record QuoteOrderLineInput(long QuoteLineId,decimal Quantity);
public sealed record ConvertQuoteToOrderRequest(IReadOnlyList<QuoteOrderLineInput>? Lines=null,DateOnly? OrderDate=null,DateOnly? DeliveryDate=null,string? ProjectCode=null,string? Description=null,string? OrderNo=null);
public sealed record ProcurementNextDocumentNo(string DocumentType,string DocumentNo);

public sealed record ProcurementPolicyDto(long Id,string BranchCode,bool AllowMultipleRfqsPerRequest,bool AllowPartialRfqLines,bool AllowMultipleQuotesPerSupplier,bool AllowMultipleOrdersPerQuote,bool AllowPartialOrderLines,bool AllowSplitAwardsAcrossSuppliers,string SupplierQuoteChannelMode,int InvitationValidityDays,bool AllowSupplierDraftSave,bool AllowSupplierQuantityChange,bool AllowSupplierRevisions,int MaximumSupplierRevisionCount,bool RequireSupplierDeliveryDate,bool AllowZeroUnitPrice,long? UpdatedBy,DateTime? UpdatedDate);
public sealed record UpdateProcurementPolicyRequest(bool AllowMultipleRfqsPerRequest,bool AllowPartialRfqLines,bool AllowMultipleQuotesPerSupplier,bool AllowMultipleOrdersPerQuote,bool AllowPartialOrderLines,bool AllowSplitAwardsAcrossSuppliers,string SupplierQuoteChannelMode,int InvitationValidityDays,bool AllowSupplierDraftSave,bool AllowSupplierQuantityChange,bool AllowSupplierRevisions,int MaximumSupplierRevisionCount,bool RequireSupplierDeliveryDate,bool AllowZeroUnitPrice);

public sealed record ProcurementWorkspaceSummary(int DraftRequests,int PendingRequests,int OpenRfqs,int SubmittedQuotes,int PendingOrders,int ApprovedOpenOrders);
public sealed record ProcurementGridRow(long Id,string DocumentType,string DocumentNo,DateOnly DocumentDate,string Status,string Subject,string? Counterparty,int LineCount,decimal TotalAmount,string CurrencyCode,DateOnly? DueDate,DateTime? CreatedDate,long? RequestId=null,string? RequestNo=null,long? RfqId=null);
public sealed record ProcurementAttachmentRow(long Id,string OwnerType,long OwnerId,string FileName,string ContentType,string Url,long FileSize,string? Caption,DateTime? CreatedDate);
public sealed record ProcurementAttachmentDownload(Stream Content,string FileName,string ContentType);
public sealed record ProcurementAttachmentUpload(Stream Content,string FileName,string? ContentType,long Length);
public sealed record ProcurementLineDetail(long Id,int LineNo,long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,decimal SecondaryQuantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? RequiredDate,string? ProjectCode,decimal OpenQuantity,long? SourceRequestLineId=null,IReadOnlyList<ProcurementAttachmentRow>? Attachments=null,string? Status=null);
public sealed record ProcurementSupplierParticipant(long? SupplierId,string SupplierCode,string SupplierName,string? InvitationStatus=null,string? RecipientEmail=null,DateTimeOffset? InvitationExpiresAtUtc=null);
public sealed record ProcurementDocumentDetail(long Id,string DocumentType,string DocumentNo,DateOnly DocumentDate,string Status,string Subject,string? Description,string? CounterpartyCode,string? CounterpartyName,string CurrencyCode,decimal ExchangeRate,DateOnly? DueDate,IReadOnlyList<ProcurementLineDetail> Lines,IReadOnlyList<ProcurementHistoryRow> History,IReadOnlyList<ProcurementSupplierParticipant>? Suppliers=null,long? RequestId=null,string? RequestNo=null,IReadOnlyList<ProcurementAttachmentRow>? Attachments=null);
public sealed record ProcurementHistoryRow(string FromStatus,string ToStatus,long ActorUserId,string? Note,DateTimeOffset ChangedAtUtc);
public sealed record SendProcurementInvitationRequest(long SupplierId,string RecipientEmail);
public sealed record ProcurementInvitationResult(long InvitationId,string Status,string RecipientEmail,DateTimeOffset ExpiresAtUtc);
public sealed record SupplierPortalLine(long RfqLineId,int LineNo,string? StockCode,string StockName,string UnitCode,decimal RequestedQuantity,DateOnly? RequiredDate,decimal QuotedQuantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? DeliveryDate);
public sealed record SupplierPortalQuote(string RfqNo,string Subject,string? BuyerMessage,string SupplierCode,string SupplierName,string Status,DateOnly ResponseDueDate,DateTimeOffset ExpiresAtUtc,string? QuoteNo,DateOnly? QuoteDate,DateOnly? ValidUntil,string CurrencyCode,decimal ExchangeRate,string? Note,int RevisionNo,bool AllowDraftSave,bool AllowQuantityChange,bool RequireDeliveryDate,bool AllowZeroUnitPrice,IReadOnlyList<SupplierPortalLine> Lines);
public sealed record SaveSupplierPortalQuoteRequest(string? QuoteNo,DateOnly? QuoteDate,DateOnly? ValidUntil,string CurrencyCode,decimal ExchangeRate,string? Note,IReadOnlyList<SupplierQuoteLineInput> Lines);
public interface IProcurementEmailSender
{
    Task SendQuoteInvitationAsync(string recipientEmail,string supplierName,string rfqNo,string subject,DateOnly responseDueDate,string portalUrl,CancellationToken cancellationToken=default);
}

// Bu sözleşme Procurement tablosunu GoodsReceipt tablolarına bağlamadan, onaylı siparişleri
// ileride IGoodsReceiptOrderSource adaptörüne sunar.
public sealed record ProcurementReceiptSourceLine(long PurchaseOrderId,long PurchaseOrderLineId,string OrderNo,int LineNo,long? StockId,string? StockCode,string StockName,string UnitCode,long? SupplierId,string SupplierCode,string SupplierName,string? ProjectCode,DateOnly OrderDate,DateOnly? DeliveryDate,decimal OrderedQuantity,decimal ReceivedQuantity,decimal OpenQuantity);

public interface IProcurementService
{
    Task<ProcurementWorkspaceSummary> GetSummaryAsync(CancellationToken ct=default);
    Task<PagedResponse<ProcurementGridRow>> GetPagedAsync(string documentType,PagedRequest request,CancellationToken ct=default);
    Task<ProcurementDocumentDetail> GetDetailAsync(string documentType,long id,CancellationToken ct=default);
    Task<ProcurementNextDocumentNo> PeekNextDocumentNoAsync(string documentType,CancellationToken ct=default);
    Task<long> CreateRequestAsync(CreateProcurementRequest request,long actorUserId,CancellationToken ct=default);
    Task TransitionRequestAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default);
    Task<long> ConvertRequestToRfqAsync(long id,ConvertRequestToRfqRequest request,long actorUserId,CancellationToken ct=default);
    Task TransitionRfqAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default);
    Task<long> CreateQuoteAsync(long rfqId,CreateSupplierQuoteRequest request,long actorUserId,CancellationToken ct=default);
    Task TransitionQuoteAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default);
    Task<long> ConvertQuoteToOrderAsync(long id,ConvertQuoteToOrderRequest request,long actorUserId,CancellationToken ct=default);
    Task<long> CreateOrderAsync(CreatePurchaseOrderRequest request,long actorUserId,CancellationToken ct=default);
    Task TransitionOrderAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default);
    Task<IReadOnlyList<ProcurementReceiptSourceLine>> GetOpenReceiptSourceLinesAsync(long? purchaseOrderId,CancellationToken ct=default);
    Task<ProcurementInvitationResult> SendInvitationAsync(long rfqId,SendProcurementInvitationRequest request,long actorUserId,CancellationToken ct=default);
    Task RevokeInvitationAsync(long rfqId,long supplierId,long actorUserId,CancellationToken ct=default);
    Task RequestQuoteRevisionAsync(long quoteId,string? note,long actorUserId,CancellationToken ct=default);
    Task<IReadOnlyList<ProcurementAttachmentRow>> ListAttachmentsAsync(string ownerType,long ownerId,CancellationToken ct=default);
    Task<ProcurementAttachmentRow> AddAttachmentAsync(string ownerType,long ownerId,ProcurementAttachmentUpload upload,string? caption,long actorUserId,CancellationToken ct=default);
    Task<ProcurementAttachmentDownload> DownloadAttachmentAsync(long attachmentId,CancellationToken ct=default);
    Task RemoveAttachmentAsync(long attachmentId,long actorUserId,CancellationToken ct=default);
}

public interface IProcurementSupplierPortalService
{
    Task<SupplierPortalQuote> GetAsync(string token,CancellationToken ct=default);
    Task SaveDraftAsync(string token,SaveSupplierPortalQuoteRequest request,CancellationToken ct=default);
    Task SubmitAsync(string token,SaveSupplierPortalQuoteRequest request,CancellationToken ct=default);
}

public interface IProcurementPolicyService
{
    Task<ProcurementPolicyDto> GetAsync(string branchCode,CancellationToken ct=default);
    Task<ProcurementPolicyDto> UpdateAsync(string branchCode,UpdateProcurementPolicyRequest request,long actorUserId,CancellationToken ct=default);
}
