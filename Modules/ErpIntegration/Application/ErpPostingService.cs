using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.ProjectSettings.Domain;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed class ErpPostingService(
    IUnitOfWork unitOfWork,
    INetsisRestClient netsisClient,
    IOptions<NetsisOptions> optionsAccessor,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ErpPostingService> logger) : IErpPostingService
{
    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web);
    private IGenericRepository<ErpPostingRecord> Postings => unitOfWork.Repository<ErpPostingRecord>();
    private IGenericRepository<ErpIntegrationAttempt> Attempts => unitOfWork.Repository<ErpIntegrationAttempt>();

    public async Task<ErpPostingResult> PostGoodsReceiptAsync(
        long id,
        Guid idempotencyKey,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var header = await unitOfWork.Repository<GoodsReceiptHeader>().Query(true)
            .Include(x => x.Lines).ThenInclude(x => x.Sources)
            .Include(x => x.SourceDocuments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
        ValidateGoodsReceiptGate(header);

        var targetWarehouse = await GetWarehouseAsync(header.TargetWarehouseId, cancellationToken);
        var request = await MapGoodsReceiptAsync(header, targetWarehouse, cancellationToken);
        return await PostAsync(
            ErpPostingSourceType.GoodsReceipt,
            header.Id,
            header.DocumentNo,
            header.BranchCode,
            idempotencyKey,
            request,
            header.ErpIntegrationStatus,
            status => header.ErpIntegrationStatus = status,
            unitOfWork.Repository<GoodsReceiptHeader>(),
            header,
            userId,
            cancellationToken);
    }

    public async Task<ErpPostingResult> PostWarehouseTransferAsync(
        long id,
        Guid idempotencyKey,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var header = await unitOfWork.Repository<WarehouseTransferHeader>().Query(true)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Lines).ThenInclude(x => x.Sources)
            .Include(x => x.SourceDocuments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Depolar arası transfer kaydı bulunamadı.");
        if (header.Status is not (WarehouseTransferStatus.Shipped
            or WarehouseTransferStatus.PartiallyReceived
            or WarehouseTransferStatus.Received
            or WarehouseTransferStatus.PartiallyPutaway
            or WarehouseTransferStatus.Completed))
            throw AppException.Conflict("ERP transfer kaydı için transferin sevk edilmiş olması gerekir.");
        if (header.ApprovalStatus is OperationApprovalStatus.Pending or OperationApprovalStatus.Rejected)
            throw AppException.Conflict("Transfer onay süreci tamamlanmadan ERP kaydı oluşturulamaz.");

        var sourceWarehouse = await GetWarehouseAsync(header.SourceWarehouseId, cancellationToken);
        var targetWarehouse = await GetWarehouseAsync(header.TargetWarehouseId, cancellationToken);
        var request = MapWarehouseTransfer(
            header, sourceWarehouse, targetWarehouse, await SendSerialsToErpAsync(cancellationToken));
        return await PostAsync(
            ErpPostingSourceType.WarehouseTransfer,
            header.Id,
            header.DocumentNo,
            header.BranchCode,
            idempotencyKey,
            request,
            header.ErpIntegrationStatus,
            status => header.ErpIntegrationStatus = status,
            unitOfWork.Repository<WarehouseTransferHeader>(),
            header,
            userId,
            cancellationToken);
    }

    public async Task<ErpPostingResult> PostShipmentAsync(
        long id,
        Guid idempotencyKey,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var header = await unitOfWork.Repository<ShipmentHeader>().Query(true)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Lines).ThenInclude(x => x.Sources)
            .Include(x => x.SourceDocuments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");
        if (header.Status != ShipmentStatus.Shipped)
            throw AppException.Conflict("ERP sevk irsaliyesi için sevkin kesinleştirilmiş olması gerekir.");
        if (header.ApprovalStatus is OperationApprovalStatus.Pending or OperationApprovalStatus.Rejected)
            throw AppException.Conflict("Sevk onay süreci tamamlanmadan ERP kaydı oluşturulamaz.");

        var sourceWarehouse = await GetWarehouseAsync(header.SourceWarehouseId, cancellationToken);
        var request = MapShipment(
            header, sourceWarehouse, await SendSerialsToErpAsync(cancellationToken));
        return await PostAsync(
            ErpPostingSourceType.Shipment,
            header.Id,
            header.DocumentNo,
            header.BranchCode,
            idempotencyKey,
            request,
            header.ErpIntegrationStatus,
            status => header.ErpIntegrationStatus = status,
            unitOfWork.Repository<ShipmentHeader>(),
            header,
            userId,
            cancellationToken);
    }

    public async Task<ErpPostingResult> GetAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        CancellationToken cancellationToken)
    {
        var entity = await Postings.FirstOrDefaultAsync(
            x => x.SourceType == sourceType && x.SourceEntityId == sourceEntityId,
            cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("ERP gönderim kaydı bulunamadı.");
        return ToResult(entity);
    }

    private async Task<ErpPostingResult> PostAsync<TEntity>(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        string sourceDocumentNo,
        string branchCode,
        Guid idempotencyKey,
        NetsisItemSlipRequest request,
        ErpIntegrationStatus currentHeaderStatus,
        Action<ErpIntegrationStatus> setHeaderStatus,
        IGenericRepository<TEntity> headerRepository,
        TEntity header,
        long userId,
        CancellationToken cancellationToken) where TEntity : class
    {
        var hash = ComputeHash(request);
        var posting = await Postings.FirstOrDefaultAsync(
            x => x.SourceType == sourceType && x.SourceEntityId == sourceEntityId,
            tracking: true,
            cancellationToken);

        if (posting?.Status == ErpPostingStatus.Succeeded) return ToResult(posting);
        if (posting?.Status == ErpPostingStatus.Processing)
            throw AppException.Conflict("Bu belge için ERP gönderimi başka bir kullanıcı veya iş tarafından yürütülüyor.");
        if (posting?.Status == ErpPostingStatus.CommitUncertain)
            throw AppException.Conflict(
                "Önceki ERP gönderiminin sonucu belirsiz. Netsis üzerinden belge kontrol edilmeden tekrar gönderim yapılamaz.");
        if (currentHeaderStatus == ErpIntegrationStatus.Succeeded && posting is null)
            throw AppException.Conflict("Belge ERP'ye gönderilmiş görünüyor ancak yerel gönderim kaydı bulunamadı. Manuel mutabakat gerekir.");

        if (posting is null)
        {
            posting = new ErpPostingRecord
            {
                BranchCode = branchCode,
                SourceType = sourceType,
                SourceEntityId = sourceEntityId,
                SourceDocumentNo = sourceDocumentNo,
                IdempotencyKey = idempotencyKey,
                RequestHash = hash,
                Status = ErpPostingStatus.Processing,
                AttemptCount = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
                CreatedBy = userId,
                TraceId = TraceId()
            };
            await Postings.AddAsync(posting, cancellationToken);
        }
        else
        {
            if (posting.IdempotencyKey == idempotencyKey
                && !string.Equals(posting.RequestHash, hash, StringComparison.Ordinal))
                throw AppException.Conflict("Aynı idempotency anahtarı farklı ERP içeriğiyle kullanılamaz.");
            posting.IdempotencyKey = idempotencyKey;
            posting.RequestHash = hash;
            posting.Status = ErpPostingStatus.Processing;
            posting.AttemptCount++;
            posting.StartedAtUtc = DateTimeOffset.UtcNow;
            posting.CompletedAtUtc = null;
            posting.LastErrorCode = null;
            posting.LastErrorMessage = null;
            posting.TraceId = TraceId();
            Postings.Update(posting);
        }

        setHeaderStatus(ErpIntegrationStatus.Processing);
        headerRepository.Update(header);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "ERP gönderim kilidi alınamadı SourceType={SourceType} SourceId={SourceId}", sourceType, sourceEntityId);
            throw AppException.Conflict("Bu belge için eş zamanlı bir ERP gönderimi başlatıldı.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        NetsisCallResult<NetsisItemSlipResponse> call;
        try
        {
            call = await netsisClient.CreateItemSlipAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            posting.Status = ErpPostingStatus.CommitUncertain;
            posting.LastErrorCode = "REQUEST_CANCELLED_COMMIT_UNCERTAIN";
            posting.LastErrorMessage = "İstek ERP yanıtı alınmadan iptal edildi; ERP kaydı manuel kontrol edilmelidir.";
            posting.CompletedAtUtc = DateTimeOffset.UtcNow;
            setHeaderStatus(ErpIntegrationStatus.CommitUncertain);
            Postings.Update(posting);
            headerRepository.Update(header);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            call = new(false, false, false, null, 0, null, null, "ERP_PRE_SEND_FAILURE", ex.Message);
        }

        var succeeded = call.TransportSucceeded && call.BusinessSucceeded;
        posting.Status = succeeded
            ? ErpPostingStatus.Succeeded
            : call.CommitUncertain ? ErpPostingStatus.CommitUncertain : ErpPostingStatus.Failed;
        posting.CompletedAtUtc = DateTimeOffset.UtcNow;
        posting.LastHttpStatusCode = call.HttpStatusCode;
        posting.LastErrorCode = succeeded ? null : call.ErrorCode;
        posting.LastErrorMessage = succeeded ? null : call.ErrorMessage;
        posting.ErpDocumentNo = call.Data?.Data?.FisNo;
        posting.ErpWaybillNo = call.Data?.Data?.BelgeNo;
        posting.ErpRecordNo = call.Data?.Data?.KayitNo;
        posting.ErpReferenceNo = call.Data?.Data?.ReferenceNumber;
        setHeaderStatus(succeeded
            ? ErpIntegrationStatus.Succeeded
            : call.CommitUncertain ? ErpIntegrationStatus.CommitUncertain : ErpIntegrationStatus.Failed);
        Postings.Update(posting);
        headerRepository.Update(header);

        await Attempts.AddAsync(new ErpIntegrationAttempt
        {
            BranchCode = branchCode,
            ErpPostingRecordId = posting.Id,
            AttemptNo = posting.AttemptCount,
            Operation = sourceType.ToString(),
            Endpoint = optionsAccessor.Value.Rest.ItemSlipsPath,
            RequestHash = hash,
            HttpStatusCode = call.HttpStatusCode,
            IsSuccessful = succeeded,
            CommitUncertain = call.CommitUncertain,
            DurationMs = call.DurationMs,
            ErrorCode = call.ErrorCode,
            ErrorMessage = call.ErrorMessage,
            ProviderResponse = call.RawResponse,
            TraceId = posting.TraceId,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = userId
        }, CancellationToken.None);
        // ERP çağrısından sonra istemci bağlantısı kopsa bile yerel sonuç mutlaka kesinleştirilmelidir.
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return ToResult(posting);
    }

    private async Task<NetsisItemSlipRequest> MapGoodsReceiptAsync(
        GoodsReceiptHeader header,
        WarehouseEntity warehouse,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(header.SupplierCodeSnapshot))
            throw AppException.Conflict("Mal kabul ERP irsaliyesi için tedarikçi kodu zorunludur.");

        var options = optionsAccessor.Value.Rest;
        var sendSerials = await SendSerialsToErpAsync(cancellationToken);
        var serials = sendSerials
            ? await unitOfWork.Repository<GoodsReceiptExecutionLine>().Query()
                .Where(x => x.Execution.GrHeaderId == header.Id
                    && x.Execution.Status == GoodsReceiptExecutionStatus.Posted
                    && x.SerialNo != null)
                .Select(x => new { x.GrLineId, x.Quantity, x.SerialNo })
                .ToListAsync(cancellationToken)
            : [];
        var lines = new List<NetsisItemSlipLine>();

        foreach (var line in header.Lines.OrderBy(x => x.LineNo))
        {
            if (line.AcceptedQuantity <= 0) continue;
            var orderNo = ResolveGoodsReceiptOrderNo(header, line);
            var lineSerials = serials.Where(x => x.GrLineId == line.Id).ToList();
            if (sendSerials && line.RequireSerial)
            {
                if (lineSerials.Count == 0 || lineSerials.Sum(x => x.Quantity) != line.AcceptedQuantity)
                    throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için kabul miktarıyla eşleşen seri kayıtları tamamlanmadan ERP irsaliyesi oluşturulamaz.");
                lines.AddRange(lineSerials.Select(serial => NewLine(
                    line.StockCodeSnapshot, serial.Quantity, warehouse.WarehouseCode, null, null,
                    line.YapCodeSnapshot, serial.SerialNo, orderNo, line.Description)));
            }
            else
            {
                lines.Add(NewLine(line.StockCodeSnapshot, line.AcceptedQuantity, warehouse.WarehouseCode,
                    null, null, line.YapCodeSnapshot, null, orderNo, line.Description));
            }
        }

        return NewRequest(
            options.GoodsReceiptDocumentType,
            options.GoodsReceiptSeries,
            header.DocumentNo,
            header.WaybillNo ?? header.ElectronicWaybillNo ?? header.DocumentNo,
            header.DocumentDate,
            header.ReceivedAtUtc,
            header.SupplierCodeSnapshot,
            warehouse,
            header.Description,
            lines);
    }

    private NetsisItemSlipRequest MapWarehouseTransfer(
        WarehouseTransferHeader header,
        WarehouseEntity sourceWarehouse,
        WarehouseEntity targetWarehouse,
        bool sendSerials)
    {
        var options = optionsAccessor.Value.Rest;
        var lines = new List<NetsisItemSlipLine>();
        foreach (var line in header.Lines.OrderBy(x => x.LineNo))
        {
            var quantity = line.ShippedQuantity;
            if (quantity <= 0) continue;
            var orderNo = ResolveTransferOrderNo(header, line);
            var serials = line.Trackings.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo) && x.ShippedQuantity > 0).ToList();
            if (sendSerials && line.RequireSerial)
            {
                if (serials.Count == 0 || serials.Sum(x => x.ShippedQuantity) != quantity)
                    throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için sevk miktarıyla eşleşen seri kayıtları tamamlanmadan ERP transferi oluşturulamaz.");
                lines.AddRange(serials.Select(serial => NewLine(line.StockCodeSnapshot, serial.ShippedQuantity,
                    null, sourceWarehouse.WarehouseCode, targetWarehouse.WarehouseCode,
                    line.YapCodeSnapshot, serial.SerialNo, orderNo, line.Description)));
            }
            else
            {
                lines.Add(NewLine(line.StockCodeSnapshot, quantity, null, sourceWarehouse.WarehouseCode,
                    targetWarehouse.WarehouseCode, line.YapCodeSnapshot, null, orderNo, line.Description));
            }
        }

        return NewRequest(options.WarehouseTransferDocumentType, options.WarehouseTransferSeries,
            header.DocumentNo, header.WaybillNo ?? header.DocumentNo, header.DocumentDate, header.ShippedAtUtc,
            null, sourceWarehouse, header.Description, lines);
    }

    private NetsisItemSlipRequest MapShipment(ShipmentHeader header, WarehouseEntity warehouse, bool sendSerials)
    {
        if (string.IsNullOrWhiteSpace(header.CustomerCodeSnapshot))
            throw AppException.Conflict("Sevk ERP irsaliyesi için müşteri kodu zorunludur.");

        var options = optionsAccessor.Value.Rest;
        var lines = new List<NetsisItemSlipLine>();
        foreach (var line in header.Lines.OrderBy(x => x.LineNo))
        {
            var quantity = line.ShippedQuantity;
            if (quantity <= 0) continue;
            var orderNo = ResolveShipmentOrderNo(header, line);
            var serials = line.Trackings.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo) && x.ShippedQuantity > 0).ToList();
            var serialTracked = line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial;
            if (sendSerials && serialTracked)
            {
                if (serials.Count == 0 || serials.Sum(x => x.ShippedQuantity) != quantity)
                    throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için sevk miktarıyla eşleşen seri kayıtları tamamlanmadan ERP irsaliyesi oluşturulamaz.");
                lines.AddRange(serials.Select(serial => NewLine(line.StockCodeSnapshot, serial.ShippedQuantity,
                    warehouse.WarehouseCode, null, null, line.YapCodeSnapshot, serial.SerialNo, orderNo, line.Description)));
            }
            else
            {
                lines.Add(NewLine(line.StockCodeSnapshot, quantity, warehouse.WarehouseCode,
                    null, null, line.YapCodeSnapshot, null, orderNo, line.Description));
            }
        }

        return NewRequest(options.ShipmentDocumentType, options.ShipmentSeries,
            header.DocumentNo, header.WaybillNo ?? header.DocumentNo, header.DocumentDate, header.ShippedAtUtc,
            header.CustomerCodeSnapshot, warehouse, header.Description, lines);
    }

    private NetsisItemSlipRequest NewRequest(
        int documentType,
        string? series,
        string documentNo,
        string waybillNo,
        DateOnly documentDate,
        DateTimeOffset? actualAtUtc,
        string? customerCode,
        WarehouseEntity warehouse,
        string? description,
        List<NetsisItemSlipLine> lines)
    {
        if (lines.Count == 0) throw AppException.Conflict("ERP belgesi için pozitif miktarlı en az bir kalem gerekir.");
        var actual = actualAtUtc?.UtcDateTime ?? documentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return new NetsisItemSlipRequest
        {
            FaturaTip = documentType,
            KayitliNumaraOtomatikGuncellensin = optionsAccessor.Value.Rest.AutoUpdateRegisteredNumber,
            Seri = series,
            FatUst = new NetsisItemSlipHeader
            {
                CariKod = customerCode,
                FisNo = documentNo,
                BelgeNo = waybillNo,
                Tarih = documentDate.ToDateTime(TimeOnly.MinValue),
                FiiliTarih = actual,
                Tip = documentType,
                SubeKodu = ParseBranchCode(warehouse.BranchCode),
                Aciklama = description,
                DepoKodu = warehouse.WarehouseCode,
                Seri = series,
                KdvDahilMi = false
            },
            Kalems = lines
        };
    }

    private static NetsisItemSlipLine NewLine(
        string stockCode,
        decimal quantity,
        int? warehouseCode,
        int? sourceWarehouseCode,
        int? targetWarehouseCode,
        string? yapCode,
        string? serialNo,
        string? orderNo,
        string? description) => new()
        {
            StokKodu = stockCode,
            Miktar = quantity,
            DepoKodu = warehouseCode,
            CikisDepoKodu = sourceWarehouseCode,
            GirisDepoKodu = targetWarehouseCode,
            YapKod = yapCode,
            SeriNo = serialNo,
            SiparisNo = orderNo,
            Aciklama = description
        };

    private async Task<WarehouseEntity> GetWarehouseAsync(long id, CancellationToken cancellationToken) =>
        await unitOfWork.Repository<WarehouseEntity>().FindByIdAsync(id, cancellationToken: cancellationToken)
        ?? throw AppException.Conflict($"ERP depo eşlemesi bulunamadı. WMS depo Id: {id}");

    private async Task<bool> SendSerialsToErpAsync(CancellationToken cancellationToken)
    {
        var setting = await unitOfWork.Repository<ProjectSetting>().FirstOrDefaultAsync(
            x => x.SettingKey == "GLOBAL",
            cancellationToken: cancellationToken);
        return setting?.SendSerialsToErp ?? optionsAccessor.Value.Rest.SendSerialsToErp;
    }

    private static void ValidateGoodsReceiptGate(GoodsReceiptHeader header)
    {
        if (header.Status is not (WarehouseOperationStatus.Processed or WarehouseOperationStatus.Completed))
            throw AppException.Conflict("ERP mal kabul irsaliyesi için fiziksel kabulün tamamlanmış olması gerekir.");
        if (header.ErpPostingPolicy is GoodsReceiptErpPostingPolicy.AfterReceiptApproval
            or GoodsReceiptErpPostingPolicy.AfterAllApprovals
            && header.ApprovalStatus is not (OperationApprovalStatus.NotRequired or OperationApprovalStatus.Approved))
            throw AppException.Conflict("Mal kabul onayı tamamlanmadan ERP irsaliyesi oluşturulamaz.");
        if (header.ErpPostingPolicy is GoodsReceiptErpPostingPolicy.AfterQualityApproval
            or GoodsReceiptErpPostingPolicy.AfterAllApprovals
            && header.QualityStatus is not (OperationQualityStatus.NotRequired or OperationQualityStatus.Passed))
            throw AppException.Conflict("Kalite kararı tamamlanmadan ERP irsaliyesi oluşturulamaz.");
    }

    private static string? ResolveGoodsReceiptOrderNo(GoodsReceiptHeader header, GoodsReceiptLine line)
    {
        var sourceId = line.Sources.OrderBy(x => x.Id).Select(x => x.GrSourceDocumentId).FirstOrDefault();
        return header.SourceDocuments.FirstOrDefault(x => x.Id == sourceId)?.ExternalDocumentNo
            ?? header.SourceDocuments.OrderBy(x => x.Id).Select(x => x.ExternalDocumentNo).FirstOrDefault();
    }

    private static string? ResolveTransferOrderNo(WarehouseTransferHeader header, WarehouseTransferLine line)
    {
        var sourceId = line.Sources.OrderBy(x => x.Id).Select(x => x.WtSourceDocumentId).FirstOrDefault();
        return header.SourceDocuments.FirstOrDefault(x => x.Id == sourceId)?.ExternalDocumentNo
            ?? header.SourceDocuments.OrderBy(x => x.Id).Select(x => x.ExternalDocumentNo).FirstOrDefault();
    }

    private static string? ResolveShipmentOrderNo(ShipmentHeader header, ShipmentLine line)
    {
        var sourceId = line.Sources.OrderBy(x => x.Id).Select(x => x.ShipmentSourceDocumentId).FirstOrDefault();
        return header.SourceDocuments.FirstOrDefault(x => x.Id == sourceId)?.ExternalDocumentNo
            ?? header.SourceDocuments.OrderBy(x => x.Id).Select(x => x.ExternalDocumentNo).FirstOrDefault();
    }

    private static int ParseBranchCode(string value) =>
        int.TryParse(value, out var result) ? result : 0;

    private static string ComputeHash(NetsisItemSlipRequest request)
    {
        var json = JsonSerializer.Serialize(request, HashJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static void EnsureIdempotencyKey(Guid value)
    {
        if (value == Guid.Empty) throw AppException.BadRequest("IdempotencyKey zorunludur.");
    }

    private string TraceId() =>
        Activity.Current?.TraceId.ToString()
        ?? httpContextAccessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString("N");

    private static ErpPostingResult ToResult(ErpPostingRecord x) => new(
        x.Id, x.SourceType, x.SourceEntityId, x.SourceDocumentNo, x.Status, x.AttemptCount,
        x.ErpDocumentNo, x.ErpWaybillNo, x.ErpRecordNo, x.ErpReferenceNo,
        x.LastErrorCode, x.LastErrorMessage, x.CompletedAtUtc);
}
