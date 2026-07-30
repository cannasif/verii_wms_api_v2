using verii_wms_api_v2.Modules.IncomingInvoice.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Application;

public enum IncomingInvoiceLookupKind
{
    Automatic = 1,
    EInvoice = 2,
    EArchive = 3
}

public sealed record ELogoConnectionRow(
    long Id, string BranchCode, string Key, string DisplayName, string Vkn, string Username,
    string Source, string? EndpointUrl, string? ApplicationName, string? Version, int? TimeoutSeconds,
    bool IsActive, bool IsDefault, bool IsConfigured, string? Description,
    long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate, byte[] RowVersion);

public sealed record SaveELogoConnectionRequest(
    string BranchCode, string Key, string DisplayName, string Vkn, string Username, string? Password,
    string Source, string? EndpointUrl, string? ApplicationName, string? Version, int? TimeoutSeconds,
    bool IsActive, bool IsDefault, string? Description, byte[]? RowVersion);

public sealed record ImportIncomingInvoiceRequest(
    string BranchCode, long ConnectionId, string Uuid,
    IncomingInvoiceLookupKind InvoiceKind = IncomingInvoiceLookupKind.Automatic,
    bool IncludePdf = true);

public sealed record IncomingInvoiceGridRow(
    long Id, string BranchCode, Guid Uuid, IncomingInvoiceKind DocumentKind,
    IncomingInvoiceCaptureSource CaptureSource,
    string InvoiceNo, DateOnly IssueDate, string SupplierVknOrTckn, string SupplierName,
    string CurrencyCode, decimal PayableAmount, int LineCount, int MatchedLineCount,
    IncomingInvoiceArchiveStatus ArchiveStatus, IncomingInvoiceValidationStatus ValidationStatus,
    bool HasUbl, bool HasPdf, int GoodsReceiptCount, DateTimeOffset ImportedAtUtc,
    long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate, byte[] RowVersion);

public sealed record IncomingInvoiceLineRow(
    long Id, int LineNo, string ExternalLineId, string StockCode, string? BuyerStockCode,
    string StockName, string? Description, decimal Quantity, string UnitCode, decimal UnitPrice,
    decimal LineExtensionAmount, decimal TaxRate, decimal TaxAmount, long? StockId,
    long? SupplierStockMappingId, decimal ConversionFactor, decimal SystemQuantity,
    string? SystemStockCode, string? SystemStockName, string? SystemUnitCode,
    decimal? RecognitionConfidence,
    IncomingInvoiceLineMatchStatus MatchStatus, string? MatchMessage,
    decimal LinkedQuantity, decimal RemainingQuantity);

public sealed record IncomingInvoiceDocumentRow(
    long Id, IncomingInvoiceDocumentFormat Format, string FileName, string ContentType,
    long FileSize, string Sha256, DateTimeOffset StoredAtUtc);

public sealed record IncomingInvoiceGoodsReceiptLinkRow(
    long Id, long GoodsReceiptId, string DocumentNo, decimal LinkedQuantity,
    DateTimeOffset LinkedAtUtc, long LinkedBy);

public sealed record IncomingInvoiceDetail(
    IncomingInvoiceGridRow Header, string? ProfileId, string InvoiceTypeCode,
    TimeOnly? IssueTime, string? OrderReferenceNo, string? DespatchReferenceNo,
    string CustomerVknOrTckn, string CustomerName, string? SupplierTaxOffice,
    long? SupplierCustomerId, string? SupplierCustomerCode, string? SupplierCustomerName,
    decimal LineExtensionAmount, decimal TaxExclusiveAmount,
    decimal TaxAmount, decimal TaxInclusiveAmount, decimal AllowanceTotalAmount,
    string? ValidationMessage, decimal? RecognitionConfidence,
    string SourceHash, DateTimeOffset LastSynchronizedAtUtc,
    IReadOnlyList<IncomingInvoiceLineRow> Lines,
    IReadOnlyList<IncomingInvoiceDocumentRow> Documents,
    IReadOnlyList<IncomingInvoiceGoodsReceiptLinkRow> GoodsReceipts);

public sealed record IncomingInvoiceImportResult(
    long Id, Guid Uuid, string InvoiceNo, IncomingInvoiceKind DocumentKind,
    IncomingInvoiceArchiveStatus ArchiveStatus, int LineCount, int MatchedLineCount,
    bool HasPdf, bool Replayed);

public sealed record MatchIncomingInvoiceRequest(
    string BranchCode,
    long SupplierId,
    bool AllowBuyerStockCodeFallback = true);

public sealed record IncomingInvoiceMatchResult(
    long IncomingInvoiceId,
    long SupplierId,
    int LineCount,
    int MatchedLineCount,
    int UnmatchedLineCount,
    IncomingInvoiceArchiveStatus ArchiveStatus);

public sealed record IncomingInvoiceOcrStatus(
    bool IsConfigured,
    string Provider,
    string Message,
    IReadOnlyList<string> SupportedContentTypes,
    long MaximumFileSizeBytes);

public sealed record OcrInvoiceUpload(
    string BranchCode,
    long SupplierId,
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record IncomingInvoiceGoodsReceiptLineRequest(
    long IncomingInvoiceLineId, decimal Quantity);

public sealed record CreateIncomingInvoiceGoodsReceiptRequest(
    Guid IdempotencyKey,
    string BranchCode,
    long SupplierId,
    long DocumentSeriesId,
    long TargetWarehouseId,
    long ReceivingLocationId,
    bool IsElectronicWaybill,
    string WaybillNo,
    DateOnly WaybillDate,
    DateTimeOffset? PlannedArrivalAtUtc,
    GoodsReceiptLabelStrategy LabelStrategy,
    byte Priority,
    string? Description,
    IReadOnlyList<long>? AssignedUserIds,
    IReadOnlyList<IncomingInvoiceGoodsReceiptLineRequest> Lines);

public sealed record IncomingInvoiceGoodsReceiptResult(
    long IncomingInvoiceId,
    long GoodsReceiptId,
    string DocumentNo,
    long TaskId,
    string TaskNo,
    int LineCount,
    decimal LinkedQuantity,
    IncomingInvoiceArchiveStatus ArchiveStatus,
    bool Replayed);

public sealed record IncomingInvoiceFile(Stream Content, string ContentType, string FileName);

public interface IELogoConnectionService
{
    Task<IReadOnlyList<ELogoConnectionRow>> GetSelectableAsync(string branchCode, CancellationToken ct = default);
    Task<PagedResponse<ELogoConnectionRow>> GetPagedAsync(string branchCode, PagedRequest request, CancellationToken ct = default);
    Task<ELogoConnectionRow> GetAsync(long id, string branchCode, CancellationToken ct = default);
    Task<ELogoConnectionRow> CreateAsync(SaveELogoConnectionRequest request, CancellationToken ct = default);
    Task<ELogoConnectionRow> UpdateAsync(long id, SaveELogoConnectionRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, string branchCode, CancellationToken ct = default);
}

public interface IIncomingInvoiceService
{
    Task<IncomingInvoiceImportResult> ImportAsync(ImportIncomingInvoiceRequest request, long actor, CancellationToken ct = default);
    Task<PagedResponse<IncomingInvoiceGridRow>> GetPagedAsync(string branchCode, PagedRequest request, CancellationToken ct = default);
    Task<IncomingInvoiceDetail> GetAsync(long id, string branchCode, CancellationToken ct = default);
    Task<IncomingInvoiceFile> OpenDocumentAsync(long id, IncomingInvoiceDocumentFormat format, string branchCode, CancellationToken ct = default);
    Task<IncomingInvoiceMatchResult> MatchAsync(
        long id, MatchIncomingInvoiceRequest request, long actor,
        CancellationToken ct = default);
    Task<IncomingInvoiceOcrStatus> GetOcrStatusAsync(CancellationToken ct = default);
    Task<IncomingInvoiceImportResult> ImportOcrAsync(
        OcrInvoiceUpload upload, long actor, CancellationToken ct = default);
    Task<IncomingInvoiceGoodsReceiptResult> CreateGoodsReceiptAsync(
        long id, CreateIncomingInvoiceGoodsReceiptRequest request, long actor,
        CancellationToken ct = default);
}

public sealed record ELogoFetchedInvoice(
    long ConnectionId, string OwnerVkn, Guid Uuid, IncomingInvoiceKind DocumentKind,
    string Xml, string XmlFileName, byte[]? Pdf, string? PdfFileName, ParsedIncomingInvoice Invoice,
    string SourceMethod, string? Warning);

public sealed record ParsedIncomingInvoice(
    string ProfileId, string InvoiceNo, string InvoiceTypeCode, DateOnly IssueDate, TimeOnly? IssueTime,
    string CurrencyCode, string? OrderReferenceNo, string? DespatchReferenceNo,
    ParsedInvoiceParty Supplier, ParsedInvoiceParty Customer,
    decimal LineExtensionAmount, decimal TaxExclusiveAmount, decimal TaxAmount,
    decimal TaxInclusiveAmount, decimal AllowanceTotalAmount, decimal PayableAmount,
    IReadOnlyList<ParsedIncomingInvoiceLine> Lines);

public sealed record ParsedInvoiceParty(
    string VknOrTckn, string Name, string? TaxOffice, string? City, string? District,
    string? Country, string? AddressLine);

public sealed record ParsedIncomingInvoiceLine(
    int LineNo, string ExternalLineId, string StockCode, string? BuyerStockCode,
    string StockName, string? Description, decimal Quantity, string UnitCode,
    decimal UnitPrice, decimal LineExtensionAmount, decimal TaxRate, decimal TaxAmount);

public interface IELogoPostboxClient
{
    Task<ELogoFetchedInvoice> FetchAsync(
        long connectionId, string branchCode, string uuid, IncomingInvoiceLookupKind kind,
        bool includePdf, CancellationToken ct = default);
}

public interface IIncomingInvoiceDocumentStorage
{
    Task<string> SaveAsync(long invoiceId, byte[] content, string contentType, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);
    void Delete(string storagePath);
}

public sealed record OcrAnalyzedInvoice(
    ParsedIncomingInvoice Invoice,
    decimal? Confidence,
    IReadOnlyList<decimal?> LineConfidences,
    string ProviderOperationId);

public interface IIncomingInvoiceOcrClient
{
    IncomingInvoiceOcrStatus Status { get; }
    Task<OcrAnalyzedInvoice> AnalyzeAsync(
        byte[] content, string contentType, CancellationToken ct = default);
}
