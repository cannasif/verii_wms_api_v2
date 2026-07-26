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

namespace verii_wms_api_v2.Modules.IncomingInvoice.Application;

public sealed class IncomingInvoiceService(
    IUnitOfWork unitOfWork,
    IELogoPostboxClient postboxClient,
    IIncomingInvoiceDocumentStorage documentStorage,
    IGoodsReceiptOperationsService goodsReceiptOperations,
    IAuditLogWriter audit) : IIncomingInvoiceService
{
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

                var stockCodes = fetched.Invoice.Lines
                    .SelectMany(x => new[] { x.StockCode, x.BuyerStockCode })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var stocks = await unitOfWork.Repository<StockEntity>().Query()
                    .Where(x => x.BranchCode == branch && stockCodes.Contains(x.ErpStockCode))
                    .ToListAsync(token);
                var stockMap = stocks.GroupBy(x => NormalizeCode(x.ErpStockCode))
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
                var now = DateTimeOffset.UtcNow;
                var warnings = new List<string>();
                if (!string.IsNullOrWhiteSpace(fetched.Warning)) warnings.Add(fetched.Warning);
                var header = BuildHeader(branch, fetched, sourceHash, now);
                await Headers.AddAsync(header, token);
                await unitOfWork.SaveChangesAsync(token);

                var lineEntities = new List<IncomingInvoiceLine>(fetched.Invoice.Lines.Count);
                for (var index = 0; index < fetched.Invoice.Lines.Count; index++)
                {
                    var source = fetched.Invoice.Lines[index];
                    var effectiveCode = FirstNonEmpty(source.StockCode, source.BuyerStockCode);
                    stockMap.TryGetValue(NormalizeCode(effectiveCode), out var stock);
                    var matchMessage = stock is null
                        ? string.IsNullOrWhiteSpace(effectiveCode)
                            ? "UBL kaleminde satıcı/alıcı stok kodu bulunmuyor."
                            : $"{effectiveCode} stok kodu WMS stok aynasında bulunamadı."
                        : null;
                    if (matchMessage is not null) warnings.Add($"{index + 1}. kalem: {matchMessage}");
                    lineEntities.Add(new IncomingInvoiceLine
                    {
                        BranchCode = branch,
                        IncomingInvoiceId = header.Id,
                        LineNo = index + 1,
                        ExternalLineId = Limit(source.ExternalLineId, 50) ?? (index + 1).ToString(),
                        StockCode = Limit(effectiveCode, 100) ?? string.Empty,
                        BuyerStockCode = Limit(source.BuyerStockCode, 100),
                        StockName = Limit(source.StockName, 500) ?? string.Empty,
                        Description = Limit(source.Description, 2000),
                        Quantity = source.Quantity,
                        UnitCode = Limit(source.UnitCode, 20) ?? string.Empty,
                        UnitPrice = source.UnitPrice,
                        LineExtensionAmount = source.LineExtensionAmount,
                        TaxRate = source.TaxRate,
                        TaxAmount = source.TaxAmount,
                        StockId = stock?.Id,
                        MatchStatus = stock is null
                            ? IncomingInvoiceLineMatchStatus.Unmatched
                            : IncomingInvoiceLineMatchStatus.StockMatched,
                        MatchMessage = matchMessage
                    });
                }
                await unitOfWork.Repository<IncomingInvoiceLine>().AddRangeAsync(lineEntities, token);
                header.ArchiveStatus = IncomingInvoiceArchiveStatus.NeedsReview;
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
        var search = request.Search?.Trim();
        var lines = unitOfWork.Repository<IncomingInvoiceLine>().Query();
        var documents = unitOfWork.Repository<IncomingInvoiceDocument>().Query();
        var links = unitOfWork.Repository<IncomingInvoiceGoodsReceiptLink>().Query();
        var query = Headers.Query().Where(x => x.BranchCode == branch
                && (string.IsNullOrWhiteSpace(search)
                    || x.InvoiceNo.Contains(search)
                    || x.Uuid.ToString().Contains(search)
                    || x.SupplierVknOrTckn.Contains(search)
                    || x.SupplierName.Contains(search)
                    || (x.OrderReferenceNo != null && x.OrderReferenceNo.Contains(search))
                    || (x.DespatchReferenceNo != null && x.DespatchReferenceNo.Contains(search))))
            .Select(x => new IncomingInvoiceGridRow(
                x.Id, x.BranchCode, x.Uuid, x.DocumentKind, x.InvoiceNo, x.IssueDate,
                x.SupplierVknOrTckn, x.SupplierName, x.CurrencyCode, x.PayableAmount,
                lines.Count(line => line.IncomingInvoiceId == x.Id),
                lines.Count(line => line.IncomingInvoiceId == x.Id && line.StockId != null),
                x.ArchiveStatus, x.ValidationStatus,
                documents.Any(document => document.IncomingInvoiceId == x.Id
                    && document.Format == IncomingInvoiceDocumentFormat.UblXml),
                documents.Any(document => document.IncomingInvoiceId == x.Id
                    && document.Format == IncomingInvoiceDocumentFormat.Pdf),
                links.Count(link => link.IncomingInvoiceId == x.Id),
                x.ImportedAtUtc, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate, x.RowVersion));
        return await query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(IncomingInvoiceGridRow.ImportedAtUtc))
            .ToPagedResponseAsync(request, ct);
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
        var lines = await unitOfWork.Repository<IncomingInvoiceLine>().Query()
            .Where(x => x.IncomingInvoiceId == id).OrderBy(x => x.LineNo)
            .Select(x => new IncomingInvoiceLineRow(
                x.Id, x.LineNo, x.ExternalLineId, x.StockCode, x.BuyerStockCode,
                x.StockName, x.Description, x.Quantity, x.UnitCode, x.UnitPrice,
                x.LineExtensionAmount, x.TaxRate, x.TaxAmount, x.StockId,
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
        var grid = new IncomingInvoiceGridRow(
            header.Id, header.BranchCode, header.Uuid, header.DocumentKind,
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
            header.LineExtensionAmount, header.TaxExclusiveAmount, header.TaxAmount,
            header.TaxInclusiveAmount, header.AllowanceTotalAmount, header.ValidationMessage,
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
                    requestedById[line.Id].Quantity,
                    string.IsNullOrWhiteSpace(line.UnitCode) ? "ADET" : line.UnitCode,
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
