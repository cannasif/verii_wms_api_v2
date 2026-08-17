using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.IncomingInvoice.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Application;

public sealed class IncomingInvoiceService(
    IUnitOfWork unitOfWork,
    IELogoPostboxClient postboxClient,
    IIncomingInvoiceDocumentStorage documentStorage,
    IGoodsReceiptOperationsService goodsReceiptOperations,
    ISupplierStockMappingService supplierStockMappings,
    IIncomingInvoiceOcrClient ocrClient,
    IAuditLogWriter audit) : IIncomingInvoiceService
{
    private static readonly IReadOnlyDictionary<string,string> GridSearchColumns=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]=nameof(IncomingInvoiceGridRow.Id),["invoiceNo"]=nameof(IncomingInvoiceGridRow.InvoiceSearchText),
        ["supplierVknOrTckn"]=nameof(IncomingInvoiceGridRow.SupplierVknOrTckn),["supplierName"]=nameof(IncomingInvoiceGridRow.SupplierName),
        ["payableAmount"]=nameof(IncomingInvoiceGridRow.PayableSearchText),["lineCount"]=nameof(IncomingInvoiceGridRow.LineProgressSearchText)
    };
    private static readonly string[] DefaultGridSearchColumns=["invoiceNo","supplierVknOrTckn","supplierName"];
    private static readonly HashSet<string> LineSummaryColumns=new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(IncomingInvoiceGridRow.LineCount),nameof(IncomingInvoiceGridRow.MatchedLineCount),
        nameof(IncomingInvoiceGridRow.LineProgressSearchText)
    };
    private static readonly HashSet<string> DocumentSummaryColumns=new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(IncomingInvoiceGridRow.HasUbl),nameof(IncomingInvoiceGridRow.HasPdf)
    };
    private static readonly HashSet<string> LinkSummaryColumns=new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(IncomingInvoiceGridRow.GoodsReceiptCount)
    };
    private IGenericRepository<IncomingInvoiceHeader> Headers =>
        unitOfWork.Repository<IncomingInvoiceHeader>();

    public async Task<IncomingInvoiceImportResult> ImportAsync(
        ImportIncomingInvoiceRequest request, long actor, CancellationToken ct = default)
    {
        var branch = ELogoConnectionService.NormalizeBranch(request.BranchCode);
        if (!Guid.TryParse(request.Uuid?.Trim(), out var uuid))
            throw AppException.BadRequest("Geçerli bir fatura UUID değeri girin.");
        var connection = await unitOfWork.Repository<ELogoConnection>().Query()
            .FirstOrDefaultAsync(x => x.Id == request.ConnectionId
                && x.BranchCode == branch && x.IsActive, ct)
            ?? throw AppException.NotFound("Aktif eLogo bağlantı tanımı bulunamadı.");
        var existing = await Headers.Query()
            .Include(x => x.Lines).Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.BranchCode == branch
                && x.OwnerVkn == connection.Vkn && x.Uuid == uuid, ct);
        if (existing is not null) return ImportResult(existing, replayed: true);

        var fetched = await postboxClient.FetchAsync(
            connection.Id, branch, uuid.ToString(), request.InvoiceKind, request.IncludePdf, ct);
        var xmlBytes = Encoding.UTF8.GetBytes(fetched.Xml);
        var sourceHash = Sha256(xmlBytes);
        var savedPaths = new List<string>();
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var raceWinner = await Headers.Query()
                    .Include(x => x.Lines).Include(x => x.Documents)
                    .FirstOrDefaultAsync(x => x.BranchCode == branch
                        && x.OwnerVkn == fetched.OwnerVkn && x.Uuid == fetched.Uuid, token);
                if (raceWinner is not null) return ImportResult(raceWinner, replayed: true);

                var supplierTaxId = fetched.Invoice.Supplier.VknOrTckn?.Trim();
                var inferredSupplierIds = string.IsNullOrWhiteSpace(supplierTaxId)
                    ? []
                    : await Headers.Query()
                        .Where(x => x.BranchCode == branch
                            && x.SupplierVknOrTckn == supplierTaxId
                            && x.SupplierCustomerId != null)
                        .Select(x => x.SupplierCustomerId!.Value)
                        .Distinct()
                        .Take(2)
                        .ToListAsync(token);
                var inferredSupplierId = inferredSupplierIds.Count == 1
                    ? inferredSupplierIds[0]
                    : (long?)null;
                var buyerStockCodes = fetched.Invoice.Lines
                    .Select(x => x.BuyerStockCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var stocks = await unitOfWork.Repository<StockEntity>().Query()
                    .Where(x => x.BranchCode == branch && buyerStockCodes.Contains(x.ErpStockCode))
                    .ToListAsync(token);
                var stockMap = stocks.GroupBy(x => NormalizeCode(x.ErpStockCode))
                    .Where(x => x.Count() == 1)
                    .ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
                var now = DateTimeOffset.UtcNow;
                var warnings = new List<string>();
                if (!string.IsNullOrWhiteSpace(fetched.Warning)) warnings.Add(fetched.Warning);
                var header = BuildHeader(branch, fetched, sourceHash, now);
                header.SupplierCustomerId = inferredSupplierId;
                await Headers.AddAsync(header, token);
                await unitOfWork.SaveChangesAsync(token);

                var lineEntities = new List<IncomingInvoiceLine>(fetched.Invoice.Lines.Count);
                for (var index = 0; index < fetched.Invoice.Lines.Count; index++)
                {
                    var source = fetched.Invoice.Lines[index];
                    SupplierStockResolution? resolution = null;
                    if (inferredSupplierId.HasValue && !string.IsNullOrWhiteSpace(source.StockCode))
                        resolution = await supplierStockMappings.ResolveAsync(
                            branch, inferredSupplierId.Value, source.StockCode, token);
                    stockMap.TryGetValue(NormalizeCode(source.BuyerStockCode), out var buyerStock);
                    var stockId = resolution?.StockId ?? buyerStock?.Id;
                    var matchMessage = stockId.HasValue
                        ? resolution is not null
                            ? "Tedarikçi stok eşlemesi uygulandı."
                            : "UBL alıcı stok kodu WMS stok kartıyla eşleşti."
                        : inferredSupplierId.HasValue
                            ? $"{FirstNonEmpty(source.StockCode, source.BuyerStockCode)} için aktif tedarikçi stok eşlemesi bulunamadı."
                            : "ERP tedarikçisi doğrulanmalı ve fatura kalemleri eşleştirilmelidir.";
                    if (!stockId.HasValue) warnings.Add($"{index + 1}. kalem: {matchMessage}");
                    lineEntities.Add(new IncomingInvoiceLine
                    {
                        BranchCode = branch,
                        IncomingInvoiceId = header.Id,
                        LineNo = index + 1,
                        ExternalLineId = Limit(source.ExternalLineId, 50) ?? (index + 1).ToString(),
                        StockCode = Limit(source.StockCode, 100) ?? string.Empty,
                        BuyerStockCode = Limit(source.BuyerStockCode, 100),
                        StockName = Limit(source.StockName, 500) ?? string.Empty,
                        Description = Limit(source.Description, 2000),
                        Quantity = source.Quantity,
                        UnitCode = Limit(source.UnitCode, 20) ?? string.Empty,
                        UnitPrice = source.UnitPrice,
                        LineExtensionAmount = source.LineExtensionAmount,
                        TaxRate = source.TaxRate,
                        TaxAmount = source.TaxAmount,
                        StockId = stockId,
                        SupplierStockMappingId = resolution?.MappingId,
                        ConversionFactor = resolution?.ConversionFactor ?? 1m,
                        MatchStatus = stockId is null
                            ? IncomingInvoiceLineMatchStatus.Unmatched
                            : IncomingInvoiceLineMatchStatus.Ready,
                        MatchMessage = matchMessage
                    });
                }
                await unitOfWork.Repository<IncomingInvoiceLine>().AddRangeAsync(lineEntities, token);
                var readyForReceipt = inferredSupplierId.HasValue
                    && lineEntities.All(x => x.StockId.HasValue);
                header.ArchiveStatus = readyForReceipt
                    ? IncomingInvoiceArchiveStatus.ReadyForReceipt
                    : IncomingInvoiceArchiveStatus.NeedsReview;
                if (!inferredSupplierId.HasValue)
                    warnings.Add("ERP tedarikçisi kullanıcı tarafından doğrulanmalıdır.");
                header.ValidationStatus = warnings.Count == 0
                    ? IncomingInvoiceValidationStatus.Parsed
                    : IncomingInvoiceValidationStatus.Warning;
                header.ValidationMessage = warnings.Count == 0
                    ? "UBL ayrıştırıldı. GİB şema/schematron ve mali mühür doğrulaması bu aşamada yapılmadı."
                    : Limit(string.Join(" ", warnings.Distinct()), 1000);

                var xmlPath = await documentStorage.SaveAsync(
                    header.Id, xmlBytes, "application/xml", token);
                savedPaths.Add(xmlPath);
                var documents = new List<IncomingInvoiceDocument>
                {
                    NewDocument(header, IncomingInvoiceDocumentFormat.UblXml,
                        fetched.XmlFileName, "application/xml", xmlPath, xmlBytes, now)
                };
                if (fetched.Pdf is { Length: > 0 })
                {
                    var pdfPath = await documentStorage.SaveAsync(
                        header.Id, fetched.Pdf, "application/pdf", token);
                    savedPaths.Add(pdfPath);
                    documents.Add(NewDocument(header, IncomingInvoiceDocumentFormat.Pdf,
                        fetched.PdfFileName ?? $"{fetched.Uuid}.pdf",
                        "application/pdf", pdfPath, fetched.Pdf, now));
                }
                await unitOfWork.Repository<IncomingInvoiceDocument>().AddRangeAsync(documents, token);
                await unitOfWork.SaveChangesAsync(token);
                await audit.WriteAsync(new AuditLogWriteEntry(
                    "incoming-invoice.import", nameof(IncomingInvoiceHeader), header.Id.ToString(),
                    "Succeeded", "incoming-invoice",
                    NewValues: new
                    {
                        header.Uuid, header.InvoiceNo, header.DocumentKind, header.SupplierVknOrTckn,
                        header.SupplierName, LineCount = lineEntities.Count,
                        MatchedLineCount = lineEntities.Count(x => x.StockId.HasValue),
                        HasPdf = fetched.Pdf is { Length: > 0 }, header.SourceHash
                    },
                    ChangedFields:
                    [
                        "Uuid", "InvoiceNo", "DocumentKind", "Supplier", "Lines",
                        "Documents", "SourceHash", "ArchiveStatus", "ValidationStatus"
                    ]), token);
                header.Lines = lineEntities;
                header.Documents = documents;
                return ImportResult(header, replayed: false);
            }, ct);
        }
        catch
        {
            foreach (var path in savedPaths)
            {
                try { documentStorage.Delete(path); }
                catch { /* Best-effort orphan cleanup; the original error remains authoritative. */ }
            }
            throw;
        }
    }

    public async Task<PagedResponse<IncomingInvoiceGridRow>> GetPagedAsync(
        string branchCode, PagedRequest request, CancellationToken ct = default)
    {
        var branch = ELogoConnectionService.NormalizeBranch(branchCode);
        var lines = unitOfWork.Repository<IncomingInvoiceLine>().Query();
        var documents = unitOfWork.Repository<IncomingInvoiceDocument>().Query();
        var links = unitOfWork.Repository<IncomingInvoiceGoodsReceiptLink>().Query();
        var headers=Headers.Query();
        var query=BuildPagedQuery(branch,request,headers,lines,documents,links);
        var countQuery=BuildCountQuery(branch,request,headers,lines,documents,links);
        var page=await query.ToPagedResponseAsync(countQuery,request,ct);
        if(page.Items.Count==0)return page;
        return new PagedResponse<IncomingInvoiceGridRow>
        {
            Items=await EnrichSummariesAsync(page.Items,lines,documents,links,ct),
            TotalCount=page.TotalCount,PageNumber=page.PageNumber,PageSize=page.PageSize
        };
    }

    internal static IQueryable<IncomingInvoiceGridRow> BuildPagedQuery(string branch,PagedRequest request,
        IQueryable<IncomingInvoiceHeader> headers,IQueryable<IncomingInvoiceLine> lines,
        IQueryable<IncomingInvoiceDocument> documents,IQueryable<IncomingInvoiceGoodsReceiptLink> links)
    {
        var query=BuildGridRows(branch,request,headers,lines,documents,links,
            RequiresInMainQuery(request,LineSummaryColumns),RequiresInMainQuery(request,DocumentSummaryColumns),RequiresInMainQuery(request,LinkSummaryColumns));
        return query.ApplySearch(request,GridSearchColumns,DefaultGridSearchColumns).ApplyAdvancedFilters(request)
            .ApplySort(request,nameof(IncomingInvoiceGridRow.ImportedAtUtc))
            .Select(x=>new IncomingInvoiceGridRow(x.Id,x.BranchCode,x.Uuid,x.DocumentKind,x.CaptureSource,x.InvoiceNo,x.IssueDate,
                x.SupplierVknOrTckn,x.SupplierName,x.CurrencyCode,x.PayableAmount,x.LineCount,x.MatchedLineCount,x.ArchiveStatus,
                x.ValidationStatus,x.HasUbl,x.HasPdf,x.GoodsReceiptCount,x.ImportedAtUtc,x.CreatedBy,x.CreatedDate,x.UpdatedBy,
                x.UpdatedDate,x.RowVersion,x.InvoiceSearchText,x.PayableSearchText,x.LineProgressSearchText));
    }

    internal static IQueryable<long> BuildCountQuery(string branch,PagedRequest request,
        IQueryable<IncomingInvoiceHeader> headers,IQueryable<IncomingInvoiceLine> lines,
        IQueryable<IncomingInvoiceDocument> documents,IQueryable<IncomingInvoiceGoodsReceiptLink> links)
    {
        var query=BuildGridRows(branch,request,headers,lines,documents,links,
            RequiresForCount(request,LineSummaryColumns),RequiresForCount(request,DocumentSummaryColumns),RequiresForCount(request,LinkSummaryColumns));
        return query.ApplySearch(request,GridSearchColumns,DefaultGridSearchColumns).ApplyAdvancedFilters(request).Select(x=>x.Id);
    }

    private static IQueryable<IncomingInvoiceGridProjection> BuildGridRows(string branch,PagedRequest request,
        IQueryable<IncomingInvoiceHeader> headers,IQueryable<IncomingInvoiceLine> lines,
        IQueryable<IncomingInvoiceDocument> documents,IQueryable<IncomingInvoiceGoodsReceiptLink> links,
        bool includeLines,bool includeDocuments,bool includeLinks)
    {
        var search=request.Search?.Trim();
        var baseRows=headers.Where(x=>x.BranchCode==branch&&(string.IsNullOrWhiteSpace(search)
            ||x.InvoiceNo.Contains(search)||x.Uuid.ToString().Contains(search)||x.SupplierVknOrTckn.Contains(search)
            ||x.SupplierName.Contains(search)||(x.OrderReferenceNo!=null&&x.OrderReferenceNo.Contains(search))
            ||(x.DespatchReferenceNo!=null&&x.DespatchReferenceNo.Contains(search))));
        if(!includeLines)return baseRows.Select(x=>new IncomingInvoiceGridProjection
        {
            Id=x.Id,BranchCode=x.BranchCode,Uuid=x.Uuid,DocumentKind=x.DocumentKind,CaptureSource=x.CaptureSource,
            InvoiceNo=x.InvoiceNo,IssueDate=x.IssueDate,SupplierVknOrTckn=x.SupplierVknOrTckn,SupplierName=x.SupplierName,
            CurrencyCode=x.CurrencyCode,PayableAmount=x.PayableAmount,ArchiveStatus=x.ArchiveStatus,ValidationStatus=x.ValidationStatus,
            HasUbl=includeDocuments&&documents.Any(document=>document.IncomingInvoiceId==x.Id&&document.Format==IncomingInvoiceDocumentFormat.UblXml),
            HasPdf=includeDocuments&&documents.Any(document=>document.IncomingInvoiceId==x.Id&&document.Format==IncomingInvoiceDocumentFormat.Pdf),
            GoodsReceiptCount=includeLinks?links.Count(link=>link.IncomingInvoiceId==x.Id):0,ImportedAtUtc=x.ImportedAtUtc,
            CreatedBy=x.CreatedBy,CreatedDate=x.CreatedDate,UpdatedBy=x.UpdatedBy,UpdatedDate=x.UpdatedDate,RowVersion=x.RowVersion,
            InvoiceSearchText=x.InvoiceNo+" "+x.Uuid.ToString()+" "+(x.CaptureSource==IncomingInvoiceCaptureSource.Ocr?"OCR ön inceleme OCR preview":""),
            PayableSearchText=x.PayableAmount+" "+x.CurrencyCode
        });

        var lineTotals=lines.GroupBy(line=>line.IncomingInvoiceId).Select(groupRows=>new
        {
            InvoiceId=groupRows.Key,LineCount=groupRows.Count(),MatchedLineCount=groupRows.Count(line=>line.StockId!=null)
        });
        return from x in baseRows
               join lineTotal in lineTotals on x.Id equals lineTotal.InvoiceId into lineTotalRows
               from lineTotal in lineTotalRows.DefaultIfEmpty()
               select new IncomingInvoiceGridProjection
               {
                   Id=x.Id,BranchCode=x.BranchCode,Uuid=x.Uuid,DocumentKind=x.DocumentKind,CaptureSource=x.CaptureSource,
                   InvoiceNo=x.InvoiceNo,IssueDate=x.IssueDate,SupplierVknOrTckn=x.SupplierVknOrTckn,SupplierName=x.SupplierName,
                   CurrencyCode=x.CurrencyCode,PayableAmount=x.PayableAmount,LineCount=(int?)lineTotal.LineCount??0,
                   MatchedLineCount=(int?)lineTotal.MatchedLineCount??0,ArchiveStatus=x.ArchiveStatus,ValidationStatus=x.ValidationStatus,
                   HasUbl=includeDocuments&&documents.Any(document=>document.IncomingInvoiceId==x.Id&&document.Format==IncomingInvoiceDocumentFormat.UblXml),
                   HasPdf=includeDocuments&&documents.Any(document=>document.IncomingInvoiceId==x.Id&&document.Format==IncomingInvoiceDocumentFormat.Pdf),
                   GoodsReceiptCount=includeLinks?links.Count(link=>link.IncomingInvoiceId==x.Id):0,ImportedAtUtc=x.ImportedAtUtc,
                   CreatedBy=x.CreatedBy,CreatedDate=x.CreatedDate,UpdatedBy=x.UpdatedBy,UpdatedDate=x.UpdatedDate,RowVersion=x.RowVersion,
                   InvoiceSearchText=x.InvoiceNo+" "+x.Uuid.ToString()+" "+(x.CaptureSource==IncomingInvoiceCaptureSource.Ocr?"OCR ön inceleme OCR preview":""),
                   PayableSearchText=x.PayableAmount+" "+x.CurrencyCode,
                   LineProgressSearchText=((int?)lineTotal.MatchedLineCount??0)+"/"+((int?)lineTotal.LineCount??0)
               };
    }

    private static bool RequiresForCount(PagedRequest request,IReadOnlySet<string> columns)=>
        (!string.IsNullOrWhiteSpace(request.EffectiveSearch)&&request.SearchFields.Any(columns.Contains))
        ||request.Filters.Any(filter=>columns.Contains(filter.Column));
    private static bool RequiresInMainQuery(PagedRequest request,IReadOnlySet<string> columns)=>
        RequiresForCount(request,columns)||columns.Contains(request.SortBy??string.Empty);

    private static async Task<IReadOnlyList<IncomingInvoiceGridRow>> EnrichSummariesAsync(IReadOnlyList<IncomingInvoiceGridRow> rows,
        IQueryable<IncomingInvoiceLine> lines,IQueryable<IncomingInvoiceDocument> documents,
        IQueryable<IncomingInvoiceGoodsReceiptLink> links,CancellationToken cancellationToken)
    {
        var ids=rows.Select(x=>x.Id).ToArray();
        var lineTotals=await lines.Where(x=>ids.Contains(x.IncomingInvoiceId)).GroupBy(x=>x.IncomingInvoiceId)
            .Select(groupRows=>new{InvoiceId=groupRows.Key,LineCount=groupRows.Count(),MatchedLineCount=groupRows.Count(x=>x.StockId!=null)})
            .ToDictionaryAsync(x=>x.InvoiceId,cancellationToken);
        var documentRows=await documents.Where(x=>ids.Contains(x.IncomingInvoiceId))
            .Select(x=>new{x.IncomingInvoiceId,x.Format}).ToListAsync(cancellationToken);
        var documentTotals=documentRows.GroupBy(x=>x.IncomingInvoiceId).ToDictionary(x=>x.Key,x=>new
        {
            HasUbl=x.Any(y=>y.Format==IncomingInvoiceDocumentFormat.UblXml),HasPdf=x.Any(y=>y.Format==IncomingInvoiceDocumentFormat.Pdf)
        });
        var linkTotals=await links.Where(x=>ids.Contains(x.IncomingInvoiceId)).GroupBy(x=>x.IncomingInvoiceId)
            .Select(groupRows=>new{InvoiceId=groupRows.Key,Count=groupRows.Count()}).ToDictionaryAsync(x=>x.InvoiceId,cancellationToken);
        return rows.Select(row=>
        {
            var line=lineTotals.GetValueOrDefault(row.Id);var document=documentTotals.GetValueOrDefault(row.Id);var link=linkTotals.GetValueOrDefault(row.Id);
            return row with{LineCount=line?.LineCount??0,MatchedLineCount=line?.MatchedLineCount??0,HasUbl=document?.HasUbl??false,
                HasPdf=document?.HasPdf??false,GoodsReceiptCount=link?.Count??0};
        }).ToArray();
    }

    private sealed class IncomingInvoiceGridProjection
    {
        public long Id{get;init;} public required string BranchCode{get;init;} public Guid Uuid{get;init;}
        public IncomingInvoiceKind DocumentKind{get;init;} public IncomingInvoiceCaptureSource CaptureSource{get;init;}
        public required string InvoiceNo{get;init;} public DateOnly IssueDate{get;init;} public required string SupplierVknOrTckn{get;init;}
        public required string SupplierName{get;init;} public required string CurrencyCode{get;init;} public decimal PayableAmount{get;init;}
        public int LineCount{get;init;} public int MatchedLineCount{get;init;} public IncomingInvoiceArchiveStatus ArchiveStatus{get;init;}
        public IncomingInvoiceValidationStatus ValidationStatus{get;init;} public bool HasUbl{get;init;} public bool HasPdf{get;init;}
        public int GoodsReceiptCount{get;init;} public DateTimeOffset ImportedAtUtc{get;init;} public long? CreatedBy{get;init;}
        public DateTime? CreatedDate{get;init;} public long? UpdatedBy{get;init;} public DateTime? UpdatedDate{get;init;}
        public required byte[] RowVersion{get;init;} public string? InvoiceSearchText{get;init;} public string? PayableSearchText{get;init;}
        public string? LineProgressSearchText{get;init;}
    }

    public async Task<IncomingInvoiceDetail> GetAsync(
        long id, string branchCode, CancellationToken ct = default)
    {
        var branch = ELogoConnectionService.NormalizeBranch(branchCode);
        var header = await Headers.Query().FirstOrDefaultAsync(
            x => x.Id == id && x.BranchCode == branch, ct)
            ?? throw AppException.NotFound("Gelen fatura arşiv kaydı bulunamadı.");
        var invoiceLineLinks = unitOfWork
            .Repository<IncomingInvoiceGoodsReceiptLineLink>().Query();
        var stocks = unitOfWork.Repository<StockEntity>().Query();
        var lines = await unitOfWork.Repository<IncomingInvoiceLine>().Query()
            .Where(x => x.IncomingInvoiceId == id).OrderBy(x => x.LineNo)
            .Select(x => new IncomingInvoiceLineRow(
                x.Id, x.LineNo, x.ExternalLineId, x.StockCode, x.BuyerStockCode,
                x.StockName, x.Description, x.Quantity, x.UnitCode, x.UnitPrice,
                x.LineExtensionAmount, x.TaxRate, x.TaxAmount, x.StockId,
                x.SupplierStockMappingId, x.ConversionFactor, x.Quantity * x.ConversionFactor,
                stocks.Where(stock => stock.Id == x.StockId).Select(stock => stock.ErpStockCode).FirstOrDefault(),
                stocks.Where(stock => stock.Id == x.StockId).Select(stock => stock.StockName).FirstOrDefault(),
                stocks.Where(stock => stock.Id == x.StockId).Select(stock => stock.BaseUnitCode).FirstOrDefault(),
                x.RecognitionConfidence,
                x.MatchStatus, x.MatchMessage,
                invoiceLineLinks
                    .Where(link => link.IncomingInvoiceLineId == x.Id)
                    .Sum(link => (decimal?)link.LinkedQuantity) ?? 0m,
                x.Quantity - (invoiceLineLinks
                    .Where(link => link.IncomingInvoiceLineId == x.Id)
                    .Sum(link => (decimal?)link.LinkedQuantity) ?? 0m)))
            .ToListAsync(ct);
        var documents = await unitOfWork.Repository<IncomingInvoiceDocument>().Query()
            .Where(x => x.IncomingInvoiceId == id).OrderBy(x => x.Format)
            .Select(x => new IncomingInvoiceDocumentRow(
                x.Id, x.Format, x.FileName, x.ContentType, x.FileSize, x.Sha256, x.StoredAtUtc))
            .ToListAsync(ct);
        var goodsReceipts = await (
            from link in unitOfWork.Repository<IncomingInvoiceGoodsReceiptLink>().Query()
            join receipt in unitOfWork.Repository<GoodsReceiptHeader>().Query()
                on link.GoodsReceiptId equals receipt.Id
            where link.IncomingInvoiceId == id
            orderby link.LinkedAtUtc
            select new IncomingInvoiceGoodsReceiptLinkRow(
                link.Id, link.GoodsReceiptId, receipt.DocumentNo, link.LinkedQuantity,
                link.LinkedAtUtc, link.LinkedBy))
            .ToListAsync(ct);
        var supplierCustomer = header.SupplierCustomerId.HasValue
            ? await unitOfWork.Repository<CustomerEntity>().Query()
                .Where(x => x.Id == header.SupplierCustomerId.Value && x.BranchCode == branch)
                .Select(x => new { x.CustomerCode, x.CustomerName })
                .FirstOrDefaultAsync(ct)
            : null;
        var grid = new IncomingInvoiceGridRow(
            header.Id, header.BranchCode, header.Uuid, header.DocumentKind, header.CaptureSource,
            header.InvoiceNo, header.IssueDate, header.SupplierVknOrTckn, header.SupplierName,
            header.CurrencyCode, header.PayableAmount, lines.Count,
            lines.Count(x => x.StockId.HasValue), header.ArchiveStatus, header.ValidationStatus,
            documents.Any(x => x.Format == IncomingInvoiceDocumentFormat.UblXml),
            documents.Any(x => x.Format == IncomingInvoiceDocumentFormat.Pdf),
            goodsReceipts.Count, header.ImportedAtUtc, header.CreatedBy, header.CreatedDate,
            header.UpdatedBy, header.UpdatedDate, header.RowVersion);
        return new IncomingInvoiceDetail(
            grid, header.ProfileId, header.InvoiceTypeCode, header.IssueTime,
            header.OrderReferenceNo, header.DespatchReferenceNo, header.CustomerVknOrTckn,
            header.CustomerName, header.SupplierTaxOffice, header.SupplierCustomerId,
            supplierCustomer?.CustomerCode, supplierCustomer?.CustomerName,
            header.LineExtensionAmount, header.TaxExclusiveAmount, header.TaxAmount,
            header.TaxInclusiveAmount, header.AllowanceTotalAmount, header.ValidationMessage,
            header.RecognitionConfidence,
            header.SourceHash, header.LastSynchronizedAtUtc, lines, documents, goodsReceipts);
    }

    public async Task<IncomingInvoiceFile> OpenDocumentAsync(
        long id,
        IncomingInvoiceDocumentFormat format,
        string branchCode,
        CancellationToken ct = default)
    {
        var branch = ELogoConnectionService.NormalizeBranch(branchCode);
        var document = await unitOfWork.Repository<IncomingInvoiceDocument>().Query()
            .Where(x => x.IncomingInvoiceId == id
                && x.Header.BranchCode == branch && x.Format == format)
            .Select(x => new { x.StoragePath, x.ContentType, x.FileName })
            .FirstOrDefaultAsync(ct)
            ?? throw AppException.NotFound(format == IncomingInvoiceDocumentFormat.Pdf
                ? "Faturanın PDF belgesi arşivde bulunmuyor."
                : "Faturanın UBL/XML belgesi arşivde bulunmuyor.");
        var stream = await documentStorage.OpenReadAsync(document.StoragePath, ct);
        return new IncomingInvoiceFile(stream, document.ContentType, document.FileName);
    }

    public async Task<IncomingInvoiceMatchResult> MatchAsync(
        long id,
        MatchIncomingInvoiceRequest request,
        long actor,
        CancellationToken ct = default)
    {
        var branch = ELogoConnectionService.NormalizeBranch(request.BranchCode);
        if (request.SupplierId <= 0)
            throw AppException.BadRequest("ERP tedarikçisi seçilmelidir.");
        if (!await unitOfWork.Repository<CustomerEntity>().AnyAsync(
                x => x.Id == request.SupplierId && x.BranchCode == branch, ct))
            throw AppException.BadRequest("Seçilen tedarikçi giriş yapılan şubede bulunamadı.");

        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var header = await Headers.Query(tracking: true)
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id && x.BranchCode == branch, token)
                ?? throw AppException.NotFound("Gelen fatura arşiv kaydı bulunamadı.");
            var linkedLineIds = await unitOfWork.Repository<IncomingInvoiceGoodsReceiptLineLink>()
                .Query().Where(x => x.IncomingInvoiceLine.Header.Id == id)
                .Select(x => x.IncomingInvoiceLineId).Distinct().ToListAsync(token);
            if (linkedLineIds.Count > 0 && header.SupplierCustomerId != request.SupplierId)
                throw AppException.Conflict(
                    "Mal kabule bağlanmış faturanın ERP tedarikçisi değiştirilemez.");

            var buyerCodes = header.Lines
                .Where(x => !linkedLineIds.Contains(x.Id) && x.BuyerStockCode != null)
                .Select(x => NormalizeCode(x.BuyerStockCode)).Distinct().ToArray();
            var buyerStockRows = request.AllowBuyerStockCodeFallback
                ? await unitOfWork.Repository<StockEntity>().Query()
                    .Where(x => x.BranchCode == branch && buyerCodes.Contains(x.ErpStockCode))
                    .ToListAsync(token)
                : [];
            var buyerStocks = buyerStockRows
                .GroupBy(x => NormalizeCode(x.ErpStockCode))
                .Where(x => x.Count() == 1)
                .ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);

            foreach (var line in header.Lines.Where(x => !linkedLineIds.Contains(x.Id)))
            {
                var resolution = await supplierStockMappings.ResolveAsync(
                    branch, request.SupplierId, line.StockCode, token);
                buyerStocks.TryGetValue(NormalizeCode(line.BuyerStockCode), out var buyerStock);
                line.SupplierStockMappingId = resolution?.MappingId;
                line.StockId = resolution?.StockId ?? buyerStock?.Id;
                line.ConversionFactor = resolution?.ConversionFactor ?? 1m;
                line.MatchStatus = line.StockId.HasValue
                    ? IncomingInvoiceLineMatchStatus.Ready
                    : IncomingInvoiceLineMatchStatus.Unmatched;
                line.MatchMessage = resolution is not null
                    ? "Tedarikçi stok eşlemesi uygulandı."
                    : buyerStock is not null
                        ? "UBL alıcı stok kodu WMS stok kartıyla eşleşti."
                        : "Aktif tedarikçi stok eşlemesi bulunamadı.";
            }

            header.SupplierCustomerId = request.SupplierId;
            var matched = header.Lines.Count(x => x.StockId.HasValue);
            header.ArchiveStatus = matched == header.Lines.Count
                ? IncomingInvoiceArchiveStatus.ReadyForReceipt
                : IncomingInvoiceArchiveStatus.NeedsReview;
            header.ValidationStatus = matched == header.Lines.Count
                ? IncomingInvoiceValidationStatus.Parsed
                : IncomingInvoiceValidationStatus.Warning;
            header.ValidationMessage = matched == header.Lines.Count
                ? "ERP tedarikçisi ve tüm fatura kalemleri doğrulandı."
                : $"{header.Lines.Count - matched} kalem için tedarikçi stok eşlemesi gerekiyor.";
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "incoming-invoice.match", nameof(IncomingInvoiceHeader), header.Id.ToString(),
                "Succeeded", "incoming-invoice",
                NewValues: new { request.SupplierId, MatchedLineCount = matched, header.ArchiveStatus },
                ChangedFields:
                [
                    "SupplierCustomerId", "Lines.StockId", "Lines.SupplierStockMappingId",
                    "Lines.ConversionFactor", "ArchiveStatus", "ValidationStatus"
                ]), token);
            return new IncomingInvoiceMatchResult(
                header.Id, request.SupplierId, header.Lines.Count, matched,
                header.Lines.Count - matched, header.ArchiveStatus);
        }, ct, IsolationLevel.Serializable);
    }

    public Task<IncomingInvoiceOcrStatus> GetOcrStatusAsync(
        CancellationToken ct = default) => Task.FromResult(ocrClient.Status);

    public async Task<IncomingInvoiceImportResult> ImportOcrAsync(
        OcrInvoiceUpload upload,
        long actor,
        CancellationToken ct = default)
    {
        var branch = ELogoConnectionService.NormalizeBranch(upload.BranchCode);
        if (!ocrClient.Status.IsConfigured)
            throw AppException.Conflict(ocrClient.Status.Message);
        if (upload.SupplierId <= 0 || upload.Content.Length == 0)
            throw AppException.BadRequest("ERP tedarikçisi ve belge dosyası zorunludur.");
        if (upload.Content.LongLength > ocrClient.Status.MaximumFileSizeBytes)
            throw AppException.BadRequest("Belge OCR dosya boyutu sınırını aşıyor.");
        var contentType = upload.ContentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!ocrClient.Status.SupportedContentTypes.Contains(
                contentType, StringComparer.OrdinalIgnoreCase))
            throw AppException.BadRequest("OCR için PDF, PNG, JPEG veya TIFF dosyası yükleyin.");
        ValidateOcrFileSignature(upload.Content, contentType);
        if (!await unitOfWork.Repository<CustomerEntity>().AnyAsync(
                x => x.Id == upload.SupplierId && x.BranchCode == branch, ct))
            throw AppException.BadRequest("Seçilen tedarikçi giriş yapılan şubede bulunamadı.");

        var sourceHash = Sha256(upload.Content);
        var hashBytes = SHA256.HashData(upload.Content);
        var uuid = new Guid(hashBytes[..16]);
        var existing = await Headers.Query().Include(x => x.Lines).Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.BranchCode == branch
                && x.CaptureSource == IncomingInvoiceCaptureSource.Ocr
                && x.SourceHash == sourceHash, ct);
        if (existing is not null) return ImportResult(existing, true);

        var analyzed = await ocrClient.AnalyzeAsync(upload.Content, contentType, ct);
        if (analyzed.Invoice.Lines.Count == 0)
            throw AppException.Conflict("Belgede fatura kalemi algılanamadı.");
        if (analyzed.Invoice.Lines.Count > 500)
            throw AppException.BadRequest("Tek OCR belgesinde en fazla 500 kalem desteklenir.");

        var savedPaths = new List<string>();
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var now = DateTimeOffset.UtcNow;
                var invoice = analyzed.Invoice;
                var header = BuildOcrHeader(
                    branch, upload.SupplierId, uuid, invoice, sourceHash,
                    analyzed.Confidence, now);
                await Headers.AddAsync(header, token);
                await unitOfWork.SaveChangesAsync(token);
                var lines = new List<IncomingInvoiceLine>(invoice.Lines.Count);
                for (var index = 0; index < invoice.Lines.Count; index++)
                {
                    var source = invoice.Lines[index];
                    var resolution = await supplierStockMappings.ResolveAsync(
                        branch, upload.SupplierId, source.StockCode, token);
                    lines.Add(NewOcrLine(
                        header, source, resolution,
                        analyzed.LineConfidences.ElementAtOrDefault(index)));
                }
                await unitOfWork.Repository<IncomingInvoiceLine>().AddRangeAsync(lines, token);
                var matched = lines.Count(x => x.StockId.HasValue);
                header.ArchiveStatus = matched == lines.Count
                    ? IncomingInvoiceArchiveStatus.ReadyForReceipt
                    : IncomingInvoiceArchiveStatus.NeedsReview;
                header.ValidationStatus = matched == lines.Count
                    ? IncomingInvoiceValidationStatus.Parsed
                    : IncomingInvoiceValidationStatus.Warning;
                header.ValidationMessage = matched == lines.Count
                    ? "OCR sonucu ve tedarikçi stok eşlemeleri kullanıcı onayına hazır."
                    : $"{lines.Count - matched} OCR kalemi için tedarikçi stok eşlemesi gerekiyor.";

                var path = await documentStorage.SaveAsync(header.Id, upload.Content, contentType, token);
                savedPaths.Add(path);
                var format = contentType == "application/pdf"
                    ? IncomingInvoiceDocumentFormat.Pdf
                    : IncomingInvoiceDocumentFormat.SourceImage;
                var document = NewDocument(
                    header, format, upload.FileName, contentType, path, upload.Content, now);
                await unitOfWork.Repository<IncomingInvoiceDocument>().AddAsync(document, token);
                await unitOfWork.SaveChangesAsync(token);
                await audit.WriteAsync(new AuditLogWriteEntry(
                    "incoming-invoice.ocr-import", nameof(IncomingInvoiceHeader),
                    header.Id.ToString(), "Succeeded", "incoming-invoice",
                    NewValues: new
                    {
                        header.InvoiceNo, upload.SupplierId, analyzed.ProviderOperationId,
                        header.RecognitionConfidence, LineCount = lines.Count,
                        MatchedLineCount = matched, header.SourceHash
                    },
                    ChangedFields:
                    [
                        "CaptureSource", "SupplierCustomerId", "RecognitionConfidence",
                        "Lines", "Documents", "ArchiveStatus"
                    ]), token);
                header.Lines = lines;
                header.Documents = [document];
                return ImportResult(header, false);
            }, ct);
        }
        catch
        {
            foreach (var path in savedPaths)
            {
                try { documentStorage.Delete(path); } catch { }
            }
            throw;
        }
    }

    public Task<IncomingInvoiceGoodsReceiptResult> CreateGoodsReceiptAsync(
        long id,
        CreateIncomingInvoiceGoodsReceiptRequest request,
        long actor,
        CancellationToken ct = default)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty
            || request.SupplierId <= 0 || request.DocumentSeriesId <= 0
            || request.TargetWarehouseId <= 0 || request.ReceivingLocationId <= 0
            || request.Priority is < 1 or > 5
            || request.Lines is not { Count: > 0 and <= 200 }
            || request.Lines.Any(x => x.IncomingInvoiceLineId <= 0 || x.Quantity <= 0)
            || request.Lines.GroupBy(x => x.IncomingInvoiceLineId).Any(x => x.Count() > 1))
            throw AppException.BadRequest("Faturadan mal kabul emri isteği geçersiz.");

        var branch = ELogoConnectionService.NormalizeBranch(request.BranchCode);
        var requestHash = HashRequest(request);
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var links = unitOfWork.Repository<IncomingInvoiceGoodsReceiptLink>();
            var existing = await links.Query()
                .FirstOrDefaultAsync(x => x.IncomingInvoiceId == id
                    && x.IdempotencyKey == request.IdempotencyKey, token);
            if (existing is not null)
            {
                if (!HashesMatch(existing.RequestHash, requestHash))
                    throw AppException.Conflict(
                        "Aynı idempotency anahtarı farklı bir fatura mal kabul isteğiyle kullanılmış.");
                return await ExistingGoodsReceiptResult(existing, token);
            }

            var header = await Headers.Query()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id && x.BranchCode == branch, token)
                ?? throw AppException.NotFound("Gelen fatura arşiv kaydı bulunamadı.");
            if (header.ArchiveStatus == IncomingInvoiceArchiveStatus.Rejected)
                throw AppException.Conflict("Reddedilmiş fatura mal kabul emrine dönüştürülemez.");
            if (header.SupplierCustomerId.HasValue
                && header.SupplierCustomerId.Value != request.SupplierId)
                throw AppException.Conflict(
                    "Fatura daha önce farklı bir ERP cari kartıyla eşleştirilmiş.");

            var requestedById = request.Lines.ToDictionary(x => x.IncomingInvoiceLineId);
            var selectedLines = header.Lines
                .Where(x => requestedById.ContainsKey(x.Id))
                .OrderBy(x => x.LineNo)
                .ToList();
            if (selectedLines.Count != requestedById.Count)
                throw AppException.BadRequest(
                    "Seçilen fatura kalemlerinden biri bu belgeye ait değil.");
            if (selectedLines.Any(x => !x.StockId.HasValue))
                throw AppException.Conflict(
                    "ERP stok kartıyla eşleşmeyen fatura kalemi mal kabul emrine aktarılamaz.");
            if (selectedLines.Any(x => x.ConversionFactor <= 0))
                throw AppException.Conflict(
                    "Fatura kalemlerinden birinin birim dönüşüm katsayısı geçersiz.");
            var selectedStockIds = selectedLines.Select(x => x.StockId!.Value).Distinct().ToArray();
            var systemStocks = await unitOfWork.Repository<StockEntity>().Query()
                .Where(x => selectedStockIds.Contains(x.Id) && x.BranchCode == branch)
                .ToDictionaryAsync(x => x.Id, token);
            if (systemStocks.Count != selectedStockIds.Length)
                throw AppException.Conflict(
                    "Fatura kalemlerinden birinin WMS stok kartı artık aktif değil.");

            var selectedIds = selectedLines.Select(x => x.Id).ToArray();
            var alreadyLinked = await unitOfWork
                .Repository<IncomingInvoiceGoodsReceiptLineLink>().Query()
                .Where(x => selectedIds.Contains(x.IncomingInvoiceLineId))
                .GroupBy(x => x.IncomingInvoiceLineId)
                .Select(x => new { LineId = x.Key, Quantity = x.Sum(y => y.LinkedQuantity) })
                .ToDictionaryAsync(x => x.LineId, x => x.Quantity, token);
            foreach (var line in selectedLines)
            {
                var requestedQuantity = requestedById[line.Id].Quantity;
                var remaining = line.Quantity - alreadyLinked.GetValueOrDefault(line.Id);
                if (remaining <= 0)
                    throw AppException.Conflict(
                        $"{line.LineNo}. fatura kaleminin tamamı daha önce mal kabule bağlanmış.");
                if (requestedQuantity > remaining)
                    throw AppException.Conflict(
                        $"{line.LineNo}. fatura kaleminde en fazla {remaining} {line.UnitCode} mal kabule bağlanabilir.");
            }

            var goodsReceiptRequest = new CreateManualGoodsReceiptRequest(
                request.IdempotencyKey,
                branch,
                request.DocumentSeriesId,
                request.SupplierId,
                request.TargetWarehouseId,
                request.ReceivingLocationId,
                request.WaybillDate,
                request.IsElectronicWaybill ? null : request.WaybillNo,
                request.WaybillDate,
                request.IsElectronicWaybill ? request.WaybillNo : null,
                header.InvoiceNo,
                null,
                header.SupplierName,
                null,
                null,
                null,
                null,
                request.PlannedArrivalAtUtc,
                null,
                request.LabelStrategy,
                GoodsReceiptExecutionMode.Manual,
                request.Priority,
                null,
                BuildReceiptDescription(header, request.Description),
                request.AssignedUserIds,
                selectedLines.Select(line => new ManualGoodsReceiptLineRequest(
                    line.StockId!.Value,
                    line.YapCodeId,
                    ConvertToSystemQuantity(
                        requestedById[line.Id].Quantity, line.ConversionFactor),
                    systemStocks[line.StockId.Value].BaseUnitCode,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    $"Fatura {header.InvoiceNo}, kalem {line.ExternalLineId}",
                    request.TargetWarehouseId,
                    request.ReceivingLocationId)).ToList());

            var receipt = await goodsReceiptOperations.CreateOrderlessTaskAsync(
                goodsReceiptRequest, actor, token);
            if (!receipt.TaskId.HasValue || string.IsNullOrWhiteSpace(receipt.TaskNo))
                throw AppException.Conflict(
                    "Mal kabul motoru fatura için operasyon emri oluşturamadı.");

            var receiptLines = await unitOfWork.Repository<GoodsReceiptLine>().Query()
                .Where(x => x.GrHeaderId == receipt.Id)
                .OrderBy(x => x.LineNo)
                .ToListAsync(token);
            if (receiptLines.Count != selectedLines.Count)
                throw AppException.Conflict(
                    "Oluşan mal kabul kalemleri fatura kalemleriyle eşleştirilemedi.");

            var linkedQuantity = selectedLines.Sum(
                line => requestedById[line.Id].Quantity);
            var link = new IncomingInvoiceGoodsReceiptLink
            {
                BranchCode = branch,
                IncomingInvoiceId = header.Id,
                GoodsReceiptId = receipt.Id,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = requestHash,
                LinkedQuantity = linkedQuantity,
                LinkedAtUtc = DateTimeOffset.UtcNow,
                LinkedBy = actor,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow
            };
            await links.AddAsync(link, token);
            await unitOfWork.SaveChangesAsync(token);

            var lineLinks = selectedLines.Select((line, index) =>
                new IncomingInvoiceGoodsReceiptLineLink
                {
                    BranchCode = branch,
                    IncomingInvoiceGoodsReceiptLinkId = link.Id,
                    IncomingInvoiceLineId = line.Id,
                    GoodsReceiptLineId = receiptLines[index].Id,
                    LinkedQuantity = requestedById[line.Id].Quantity,
                    CreatedBy = actor,
                    CreatedDate = DateTime.UtcNow
                }).ToList();
            await unitOfWork.Repository<IncomingInvoiceGoodsReceiptLineLink>()
                .AddRangeAsync(lineLinks, token);

            var fullyLinked = header.Lines.All(line =>
                line.StockId.HasValue
                && alreadyLinked.GetValueOrDefault(line.Id)
                    + (requestedById.TryGetValue(line.Id, out var requestedLine)
                        ? requestedLine.Quantity
                        : 0m) >= line.Quantity);
            header.SupplierCustomerId = request.SupplierId;
            header.ArchiveStatus = fullyLinked
                ? IncomingInvoiceArchiveStatus.Linked
                : IncomingInvoiceArchiveStatus.PartiallyLinked;
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsync(token);

            await audit.WriteAsync(new AuditLogWriteEntry(
                "incoming-invoice.create-goods-receipt",
                nameof(IncomingInvoiceHeader),
                header.Id.ToString(),
                "Succeeded",
                "incoming-invoice",
                NewValues: new
                {
                    header.InvoiceNo,
                    receipt.Id,
                    receipt.DocumentNo,
                    receipt.TaskId,
                    receipt.TaskNo,
                    LineCount = lineLinks.Count,
                    LinkedQuantity = linkedQuantity,
                    header.ArchiveStatus
                },
                ChangedFields:
                [
                    "GoodsReceiptLinks", "GoodsReceiptLineLinks",
                    "SupplierCustomerId", "ArchiveStatus"
                ]), token);

            return new IncomingInvoiceGoodsReceiptResult(
                header.Id,
                receipt.Id,
                receipt.DocumentNo,
                receipt.TaskId.Value,
                receipt.TaskNo,
                lineLinks.Count,
                linkedQuantity,
                header.ArchiveStatus,
                receipt.Replayed);
        }, ct, IsolationLevel.Serializable);
    }

    private static IncomingInvoiceHeader BuildHeader(
        string branch, ELogoFetchedInvoice fetched, string sourceHash, DateTimeOffset now)
    {
        var invoice = fetched.Invoice;
        return new IncomingInvoiceHeader
        {
            BranchCode = branch,
            ELogoConnectionId = fetched.ConnectionId,
            OwnerVkn = fetched.OwnerVkn,
            Uuid = fetched.Uuid,
            DocumentKind = fetched.DocumentKind,
            ProfileId = Limit(invoice.ProfileId, 50),
            InvoiceNo = Limit(invoice.InvoiceNo, 50) ?? string.Empty,
            InvoiceTypeCode = Limit(invoice.InvoiceTypeCode, 50) ?? string.Empty,
            IssueDate = invoice.IssueDate,
            IssueTime = invoice.IssueTime,
            CurrencyCode = Limit(invoice.CurrencyCode, 3) ?? "TRY",
            OrderReferenceNo = Limit(invoice.OrderReferenceNo, 100),
            DespatchReferenceNo = Limit(invoice.DespatchReferenceNo, 100),
            SupplierVknOrTckn = Limit(invoice.Supplier.VknOrTckn, 20) ?? string.Empty,
            SupplierName = Limit(invoice.Supplier.Name, 300) ?? string.Empty,
            SupplierTaxOffice = Limit(invoice.Supplier.TaxOffice, 100),
            CustomerVknOrTckn = Limit(invoice.Customer.VknOrTckn, 20) ?? string.Empty,
            CustomerName = Limit(invoice.Customer.Name, 300) ?? string.Empty,
            LineExtensionAmount = invoice.LineExtensionAmount,
            TaxExclusiveAmount = invoice.TaxExclusiveAmount,
            TaxAmount = invoice.TaxAmount,
            TaxInclusiveAmount = invoice.TaxInclusiveAmount,
            AllowanceTotalAmount = invoice.AllowanceTotalAmount,
            PayableAmount = invoice.PayableAmount,
            ArchiveStatus = IncomingInvoiceArchiveStatus.Imported,
            ValidationStatus = IncomingInvoiceValidationStatus.Parsed,
            SourceHash = sourceHash,
            ImportedAtUtc = now,
            LastSynchronizedAtUtc = now
        };
    }

    private static IncomingInvoiceHeader BuildOcrHeader(
        string branch,
        long supplierId,
        Guid uuid,
        ParsedIncomingInvoice invoice,
        string sourceHash,
        decimal? confidence,
        DateTimeOffset now) => new()
    {
        BranchCode = branch,
        CaptureSource = IncomingInvoiceCaptureSource.Ocr,
        OwnerVkn = "OCR",
        Uuid = uuid,
        DocumentKind = IncomingInvoiceKind.EArchive,
        ProfileId = Limit(invoice.ProfileId, 50),
        InvoiceNo = Limit(
            FirstNonEmpty(invoice.InvoiceNo, $"OCR-{uuid:N}"[..20]), 50)
            ?? $"OCR-{uuid:N}"[..20],
        InvoiceTypeCode = Limit(invoice.InvoiceTypeCode, 50) ?? "SATIS",
        IssueDate = invoice.IssueDate,
        IssueTime = invoice.IssueTime,
        CurrencyCode = Limit(invoice.CurrencyCode, 3) ?? "TRY",
        OrderReferenceNo = Limit(invoice.OrderReferenceNo, 100),
        DespatchReferenceNo = Limit(invoice.DespatchReferenceNo, 100),
        SupplierVknOrTckn = Limit(invoice.Supplier.VknOrTckn, 20) ?? string.Empty,
        SupplierName = Limit(invoice.Supplier.Name, 300) ?? string.Empty,
        SupplierTaxOffice = Limit(invoice.Supplier.TaxOffice, 100),
        SupplierCustomerId = supplierId,
        CustomerVknOrTckn = Limit(invoice.Customer.VknOrTckn, 20) ?? string.Empty,
        CustomerName = Limit(invoice.Customer.Name, 300) ?? string.Empty,
        LineExtensionAmount = invoice.LineExtensionAmount,
        TaxExclusiveAmount = invoice.TaxExclusiveAmount,
        TaxAmount = invoice.TaxAmount,
        TaxInclusiveAmount = invoice.TaxInclusiveAmount,
        AllowanceTotalAmount = invoice.AllowanceTotalAmount,
        PayableAmount = invoice.PayableAmount,
        ArchiveStatus = IncomingInvoiceArchiveStatus.NeedsReview,
        ValidationStatus = IncomingInvoiceValidationStatus.Warning,
        RecognitionConfidence = confidence,
        SourceHash = sourceHash,
        ImportedAtUtc = now,
        LastSynchronizedAtUtc = now
    };

    private static IncomingInvoiceLine NewOcrLine(
        IncomingInvoiceHeader header,
        ParsedIncomingInvoiceLine source,
        SupplierStockResolution? resolution,
        decimal? confidence) => new()
    {
        BranchCode = header.BranchCode,
        IncomingInvoiceId = header.Id,
        LineNo = source.LineNo,
        ExternalLineId = Limit(source.ExternalLineId, 50) ?? source.LineNo.ToString(),
        StockCode = Limit(source.StockCode, 100) ?? string.Empty,
        BuyerStockCode = Limit(source.BuyerStockCode, 100),
        StockName = Limit(source.StockName, 500) ?? string.Empty,
        Description = Limit(source.Description, 2000),
        Quantity = source.Quantity,
        UnitCode = Limit(source.UnitCode, 20) ?? string.Empty,
        UnitPrice = source.UnitPrice,
        LineExtensionAmount = source.LineExtensionAmount,
        TaxRate = source.TaxRate,
        TaxAmount = source.TaxAmount,
        StockId = resolution?.StockId,
        SupplierStockMappingId = resolution?.MappingId,
        ConversionFactor = resolution?.ConversionFactor ?? 1m,
        RecognitionConfidence = confidence,
        MatchStatus = resolution is null
            ? IncomingInvoiceLineMatchStatus.Unmatched
            : IncomingInvoiceLineMatchStatus.Ready,
        MatchMessage = resolution is null
            ? "OCR stok kodu için aktif tedarikçi stok eşlemesi bulunamadı."
            : "OCR stok koduna tedarikçi stok eşlemesi uygulandı."
    };

    private static IncomingInvoiceDocument NewDocument(
        IncomingInvoiceHeader header,
        IncomingInvoiceDocumentFormat format,
        string fileName,
        string contentType,
        string storagePath,
        byte[] content,
        DateTimeOffset now) => new()
    {
        BranchCode = header.BranchCode,
        IncomingInvoiceId = header.Id,
        Format = format,
        FileName = Limit(Path.GetFileName(fileName), 260) ?? $"invoice-{header.Uuid}",
        ContentType = contentType,
        StoragePath = storagePath,
        FileSize = content.LongLength,
        Sha256 = Sha256(content),
        StoredAtUtc = now
    };

    private static IncomingInvoiceImportResult ImportResult(
        IncomingInvoiceHeader header, bool replayed) => new(
        header.Id, header.Uuid, header.InvoiceNo, header.DocumentKind, header.ArchiveStatus,
        header.Lines.Count, header.Lines.Count(x => x.StockId.HasValue),
        header.Documents.Any(x => x.Format == IncomingInvoiceDocumentFormat.Pdf), replayed);

    private static string FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first.Trim()
        : !string.IsNullOrWhiteSpace(second) ? second.Trim() : string.Empty;
    private static string NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    internal static decimal ConvertToSystemQuantity(
        decimal supplierQuantity, decimal conversionFactor)
    {
        if (supplierQuantity <= 0 || conversionFactor <= 0)
            throw AppException.BadRequest(
                "Fatura miktarı ve dönüşüm katsayısı sıfırdan büyük olmalıdır.");
        return supplierQuantity * conversionFactor;
    }
    internal static void ValidateOcrFileSignature(byte[] content, string contentType)
    {
        var valid = contentType.ToLowerInvariant() switch
        {
            "application/pdf" => content.AsSpan().StartsWith("%PDF-"u8),
            "image/png" => content.AsSpan().StartsWith(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/jpeg" => content.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            "image/tiff" => content.AsSpan().StartsWith(new byte[] { 0x49, 0x49, 0x2A, 0x00 })
                || content.AsSpan().StartsWith(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }),
            _ => false
        };
        if (!valid)
            throw AppException.BadRequest(
                "Dosya içeriği bildirilen PDF/görsel türüyle uyuşmuyor.");
    }
    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value));
    private static string HashRequest(CreateIncomingInvoiceGoodsReceiptRequest value) =>
        Sha256(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    private static bool HashesMatch(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }
    private async Task<IncomingInvoiceGoodsReceiptResult> ExistingGoodsReceiptResult(
        IncomingInvoiceGoodsReceiptLink link,
        CancellationToken ct)
    {
        var receipt = await unitOfWork.Repository<GoodsReceiptHeader>().Query()
            .FirstOrDefaultAsync(x => x.Id == link.GoodsReceiptId, ct)
            ?? throw AppException.Conflict(
                "Faturaya bağlı mal kabul kaydı bulunamadı.");
        var task = await unitOfWork.Repository<GoodsReceiptTask>().Query()
            .FirstOrDefaultAsync(x => x.GrHeaderId == receipt.Id, ct)
            ?? throw AppException.Conflict(
                "Faturaya bağlı mal kabul emri bulunamadı.");
        var lineCount = await unitOfWork
            .Repository<IncomingInvoiceGoodsReceiptLineLink>().Query()
            .CountAsync(x => x.IncomingInvoiceGoodsReceiptLinkId == link.Id, ct);
        var status = await Headers.Query()
            .Where(x => x.Id == link.IncomingInvoiceId)
            .Select(x => x.ArchiveStatus)
            .FirstAsync(ct);
        return new IncomingInvoiceGoodsReceiptResult(
            link.IncomingInvoiceId,
            receipt.Id,
            receipt.DocumentNo,
            task.Id,
            task.TaskNo,
            lineCount,
            link.LinkedQuantity,
            status,
            true);
    }
    private static string BuildReceiptDescription(
        IncomingInvoiceHeader header,
        string? description)
    {
        var prefix = $"e-Fatura/e-Arşiv kaynağı: {header.InvoiceNo}, UUID: {header.Uuid}.";
        var value = string.IsNullOrWhiteSpace(description)
            ? prefix
            : $"{prefix} {description.Trim()}";
        return value.Length <= 1000 ? value : value[..1000];
    }
    private static string? Limit(string? value, int maximum)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized?.Length > maximum ? normalized[..maximum] : normalized;
    }
}
