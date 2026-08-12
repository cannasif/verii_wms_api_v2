using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.ErpBalanceSync.Application;
using verii_wms_api_v2.Modules.ErpBalanceSync.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.ProjectSettings.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed class ErpPostingService(
    IUnitOfWork unitOfWork,
    INetsisRestClient netsisClient,
    IGoodsReceiptOrderSource goodsReceiptOrderSource,
    INetsisReadService netsisReadService,
    IOptions<NetsisOptions> optionsAccessor,
    IOptions<ErpStockBalanceSyncOptions> balanceSyncOptionsAccessor,
    IBackgroundJobClient backgroundJobs,
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
        var externalDocumentNo = ResolveGoodsReceiptErpDocumentNo(header);
        var result = await PostAsync(
            ErpPostingSourceType.GoodsReceipt,
            header.Id,
            externalDocumentNo,
            targetWarehouse.BranchCode,
            idempotencyKey,
            request,
            header.ErpIntegrationStatus,
            status => header.ErpIntegrationStatus = status,
            unitOfWork.Repository<GoodsReceiptHeader>(),
            header,
            userId,
            cancellationToken);
        if (result.Status == ErpPostingStatus.Succeeded)
            EnqueueGoodsReceiptSuccessFollowUp(header.Id, userId);
        return result;
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
        await ValidateQualityDispositionErpPrerequisiteAsync(header, cancellationToken);
        if (!IsWarehouseTransferReadyForErp(header.Status))
            throw AppException.Conflict("ERP transfer kaydı için transferin sevk edilmiş olması gerekir.");
        if (header.ApprovalStatus is OperationApprovalStatus.Pending or OperationApprovalStatus.Rejected)
            throw AppException.Conflict("Transfer onay süreci tamamlanmadan ERP kaydı oluşturulamaz.");

        var sourceWarehouse = await GetWarehouseAsync(header.SourceWarehouseId, cancellationToken);
        var targetWarehouse = await GetWarehouseAsync(header.TargetWarehouseId, cancellationToken);
        var request = await MapWarehouseTransferAsync(
            header, sourceWarehouse, targetWarehouse, await SendSerialsToErpAsync(cancellationToken), cancellationToken);
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

    private async Task ValidateQualityDispositionErpPrerequisiteAsync(
        WarehouseTransferHeader header,
        CancellationToken cancellationToken)
    {
        if (header.BusinessContext != WarehouseTransferBusinessContext.QualityDisposition)
            return;
        if (header.Status != WarehouseTransferStatus.Completed)
            throw AppException.Conflict(
                "Kalite kaynaklı DAT, stok hareketi tamamlanmadan ERP'ye gönderilemez.");

        var receiptStatuses = await (
                from disposition in unitOfWork.Repository<QualityInspectionDisposition>().Query()
                join inspection in unitOfWork.Repository<QualityInspection>().Query()
                    on disposition.QualityInspectionId equals inspection.Id
                join receipt in unitOfWork.Repository<GoodsReceiptHeader>().Query()
                    on inspection.SourceDocumentId equals receipt.Id
                where disposition.WarehouseTransferId == header.Id
                    && inspection.SourceDocumentType == "GoodsReceipt"
                select receipt.ErpIntegrationStatus)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (receiptStatuses.Count == 0)
            throw AppException.Conflict(
                "Kalite kaynaklı DAT için bağlı mal kabul kaydı bulunamadı.");
        if (receiptStatuses.Any(status => status != ErpIntegrationStatus.Succeeded))
            throw AppException.Conflict(
                "Kalite kaynaklı DAT, bağlı mal kabul irsaliyesi ERP'de başarıyla oluşmadan gönderilemez.");
    }

    private void EnqueueGoodsReceiptSuccessFollowUp(long goodsReceiptId, long actorUserId)
    {
        try
        {
            backgroundJobs.Enqueue<IGoodsReceiptErpSuccessJob>(job =>
                job.ProcessGoodsReceiptAsync(goodsReceiptId, actorUserId, CancellationToken.None));
        }
        catch (Exception exception)
        {
            // ERP posting is already committed. Do not report it as failed because the recurring
            // recovery job will find and complete pending quality DAT records.
            logger.LogError(
                exception,
                "Goods-receipt ERP posting succeeded but quality DAT follow-up could not be queued. GoodsReceiptId={GoodsReceiptId}",
                goodsReceiptId);
        }
    }

    internal static bool IsWarehouseTransferReadyForErp(WarehouseTransferStatus status) =>
        status is WarehouseTransferStatus.Shipped
            or WarehouseTransferStatus.PartiallyReceived
            or WarehouseTransferStatus.Received
            or WarehouseTransferStatus.PartiallyPutaway
            or WarehouseTransferStatus.Completed
            or WarehouseTransferStatus.CompletedWithShortage;

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
        var request = await MapShipmentAsync(
            header, sourceWarehouse, await SendSerialsToErpAsync(cancellationToken), cancellationToken);
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

    public async Task<ErpPostingResult> PostWarehouseInboundAsync(
        long id,
        Guid idempotencyKey,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var header = await unitOfWork.Repository<WarehouseInboundHeader>().Query(true)
            .Include(x => x.Lines).ThenInclude(x => x.Sources)
            .Include(x => x.SourceDocuments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Ambar giriş kaydı bulunamadı.");
        if (header.Status is not (WarehouseOperationStatus.Processed or WarehouseOperationStatus.Completed))
            throw AppException.Conflict("ERP ambar giriş belgesi için fiziksel kabulün tamamlanmış olması gerekir.");
        if (header.ApprovalStatus is OperationApprovalStatus.Pending or OperationApprovalStatus.Rejected)
            throw AppException.Conflict("Ambar giriş onayı tamamlanmadan ERP kaydı oluşturulamaz.");

        var warehouse = await GetWarehouseAsync(header.TargetWarehouseId, cancellationToken);
        var request = await MapWarehouseInboundAsync(header, warehouse, cancellationToken);
        var externalDocumentNo = ResolveWarehouseInboundErpDocumentNo(header);
        return await PostAsync(
            ErpPostingSourceType.WarehouseInbound,
            header.Id,
            externalDocumentNo,
            warehouse.BranchCode,
            idempotencyKey,
            request,
            header.ErpIntegrationStatus,
            status => header.ErpIntegrationStatus = status,
            unitOfWork.Repository<WarehouseInboundHeader>(),
            header,
            userId,
            cancellationToken);
    }

    public async Task<ErpPostingResult> PostWarehouseOutboundAsync(
        long id,
        Guid idempotencyKey,
        long userId,
        CancellationToken cancellationToken)
    {
        EnsureIdempotencyKey(idempotencyKey);
        var header = await unitOfWork.Repository<WarehouseOutboundHeader>().Query(true)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Lines).ThenInclude(x => x.Sources)
            .Include(x => x.SourceDocuments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Ambar çıkış kaydı bulunamadı.");
        if (header.Status != WarehouseOutboundStatus.Shipped)
            throw AppException.Conflict("ERP ambar çıkış belgesi için sevkin kesinleştirilmiş olması gerekir.");
        if (header.ApprovalStatus is OperationApprovalStatus.Pending or OperationApprovalStatus.Rejected)
            throw AppException.Conflict("Ambar çıkış onayı tamamlanmadan ERP kaydı oluşturulamaz.");

        var warehouse = await GetWarehouseAsync(header.SourceWarehouseId, cancellationToken);
        var request = await MapWarehouseOutboundAsync(
            header, warehouse, await SendSerialsToErpAsync(cancellationToken), cancellationToken);
        return await PostAsync(
            ErpPostingSourceType.WarehouseOutbound,
            header.Id,
            header.DocumentNo,
            header.BranchCode,
            idempotencyKey,
            request,
            header.ErpIntegrationStatus,
            status => header.ErpIntegrationStatus = status,
            unitOfWork.Repository<WarehouseOutboundHeader>(),
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

    public async Task<ErpPostingResult> ReconcileAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        ReconcileErpPostingRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw AppException.BadRequest("Mutabakat açıklaması en az 5 karakter olmalıdır.");
        if (request.ErpDocumentExists
            && string.IsNullOrWhiteSpace(request.ErpDocumentNo)
            && string.IsNullOrWhiteSpace(request.ErpRecordNo)
            && string.IsNullOrWhiteSpace(request.ErpReferenceNo))
            throw AppException.BadRequest("ERP'de belge bulunduysa en az bir ERP belge referansı girilmelidir.");

        var posting = await Postings.FirstOrDefaultAsync(
            x => x.SourceType == sourceType && x.SourceEntityId == sourceEntityId,
            tracking: true,
            cancellationToken)
            ?? throw AppException.NotFound("ERP gönderim kaydı bulunamadı.");
        if (posting.Status != ErpPostingStatus.CommitUncertain)
            throw AppException.Conflict("Yalnızca sonucu belirsiz ERP gönderimleri manuel mutabakata alınabilir.");

        posting.Status = request.ErpDocumentExists
            ? ErpPostingStatus.Succeeded
            : ErpPostingStatus.Failed;
        posting.ErpDocumentNo = Clean(request.ErpDocumentNo);
        posting.ErpWaybillNo = Clean(request.ErpWaybillNo);
        posting.ErpRecordNo = Clean(request.ErpRecordNo);
        posting.ErpRecordId = long.TryParse(posting.ErpRecordNo, out var reconciledRecordId)
            ? reconciledRecordId
            : null;
        posting.ErpReferenceNo = Clean(request.ErpReferenceNo);
        posting.LastErrorCode = request.ErpDocumentExists ? null : "MANUAL_RECONCILIATION_NOT_FOUND";
        posting.LastErrorMessage = request.ErpDocumentExists
            ? null
            : $"ERP belgesi manuel kontrolde bulunamadı. {request.Reason.Trim()}";
        posting.CompletedAtUtc = DateTimeOffset.UtcNow;
        Postings.Update(posting);

        await SetSourceHeaderStatusAsync(
            sourceType,
            sourceEntityId,
            request.ErpDocumentExists ? ErpIntegrationStatus.Succeeded : ErpIntegrationStatus.Failed,
            cancellationToken);
        await Attempts.AddAsync(new ErpIntegrationAttempt
        {
            BranchCode = posting.BranchCode,
            ErpPostingRecordId = posting.Id,
            AttemptNo = posting.AttemptCount + 1,
            Operation = $"{sourceType}.ManualReconciliation",
            HttpMethod = "MANUAL",
            Endpoint = "Netsis/ManualReconciliation",
            RequestHash = posting.RequestHash,
            IsSuccessful = request.ErpDocumentExists,
            CommitUncertain = false,
            DurationMs = 0,
            ErrorCode = request.ErpDocumentExists ? null : "ERP_DOCUMENT_NOT_FOUND",
            ErrorMessage = request.Reason.Trim(),
            TraceId = TraceId(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = userId
        }, cancellationToken);
        posting.AttemptCount++;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResult(posting);
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
        posting.ErpRecordId = long.TryParse(posting.ErpRecordNo, out var erpRecordId)
            ? erpRecordId
            : null;
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

        if (succeeded)
            EnqueueTargetedBalanceSync(sourceType, sourceEntityId, request);

        return ToResult(posting);
    }

    private void EnqueueTargetedBalanceSync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        NetsisItemSlipRequest request)
    {
        if (!balanceSyncOptionsAccessor.Value.Enabled)
            return;

        try
        {
            var targets = request.Kalems
                .SelectMany(line => new[] { line.DepoKodu, line.GirisDepoKodu, line.CikisDepoKodu }
                    .Where(code => code.HasValue)
                    .Select(code => new ErpStockBalanceTarget(code!.Value, line.StokKodu.Trim().ToUpperInvariant())))
                .Where(x => x.WarehouseCode >= 0 && !string.IsNullOrWhiteSpace(x.StockCode))
                .Distinct()
                .ToArray();
            if (targets.Length == 0)
                return;

            var chunkSize = Math.Clamp(balanceSyncOptionsAccessor.Value.MaximumTargetCount, 1, 5000);
            foreach (var chunk in targets.Chunk(chunkSize))
            {
                var jobRequest = new ErpStockBalanceSyncJobRequest(
                    ErpStockBalanceSyncModes.Targeted,
                    ErpStockBalanceSyncTriggerSources.ErpPosting,
                    chunk,
                    $"{sourceType}:{sourceEntityId}");
                backgroundJobs.Enqueue<IErpStockBalanceSyncJobRunner>(runner =>
                    runner.RunAsync(jobRequest, CancellationToken.None));
            }
        }
        catch (Exception exception)
        {
            // ERP kaydı başarılıdır; hızlandırma işi kuyruğa alınamazsa beş dakikalık tam tur güvenlik ağıdır.
            logger.LogWarning(exception,
                "Targeted ERP balance check could not be enqueued. SourceType={SourceType} SourceId={SourceId}",
                sourceType, sourceEntityId);
        }
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
        var purchaseOrderDocuments = header.SourceDocuments
            .Where(x => x.SourceSystem == WarehouseOperationSourceSystem.Netsis
                && x.SourceDocumentType == GoodsReceiptSourceDocumentType.PurchaseOrder)
            .ToDictionary(x => x.Id);
        var orderRows = new Dictionary<(string OrderNumber, int OrderId), GoodsReceiptOrderSourceLine>();
        if (purchaseOrderDocuments.Count > 0)
        {
            var orderNumbers = string.Join(
                ',',
                purchaseOrderDocuments.Values
                    .Select(x => x.ExternalDocumentNo)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            var sourceRows = await goodsReceiptOrderSource.GetOpenLinesAsync(
                orderNumbers,
                header.SupplierCodeSnapshot,
                header.BranchCode,
                cancellationToken);
            orderRows = sourceRows.ToDictionary(
                x => (x.OrderNumber.Trim().ToUpperInvariant(), x.OrderId));
        }
        var lines = new List<NetsisItemSlipLine>();
        var usedOrderRows = new List<GoodsReceiptOrderSourceLine>();
        var orderAllocationQueues = BuildGoodsReceiptOrderAllocationQueues(
            header,
            purchaseOrderDocuments,
            orderRows);

        foreach (var line in header.Lines.OrderBy(x => x.LineNo))
        {
            var receiptQuantity = GoodsReceiptQuantityForErp(line);
            if (receiptQuantity <= 0) continue;
            var allocationKey = OrderAllocationKey(line.StockCodeSnapshot, line.YapCodeSnapshot);
            var hasOrderSource = line.Sources.Any(x => purchaseOrderDocuments.ContainsKey(x.GrSourceDocumentId));
            var allocations = hasOrderSource
                ? orderAllocationQueues.TryGetValue(allocationKey, out var allocationQueue)
                    ? AllocateOrderQuantity(allocationQueue, receiptQuantity, line.StockCodeSnapshot)
                    : throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için seçilen Netsis sipariş kaynakları ERP satır dağıtımına hazırlanamadı.")
                : [];
            usedOrderRows.AddRange(allocations.Select(x => x.OrderRow));
            var lineSerials = serials.Where(x => x.GrLineId == line.Id).ToList();
            if (sendSerials && line.RequireSerial)
            {
                if (lineSerials.Count == 0 || lineSerials.Sum(x => x.Quantity) != receiptQuantity)
                    throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için teslim alınan miktarla eşleşen seri kayıtları tamamlanmadan ERP irsaliyesi oluşturulamaz.");
                AddSerialGoodsReceiptLines(
                    lines,
                    line,
                    lineSerials.Select(x => new GoodsReceiptSerialPart(x.Quantity, x.SerialNo!)),
                    allocations,
                    warehouse.WarehouseCode);
            }
            else if (allocations.Count > 0)
                lines.AddRange(allocations.Select(allocation => NewLine(
                    line.StockCodeSnapshot,
                    allocation.Quantity,
                    warehouse.WarehouseCode,
                    null,
                    null,
                    line.YapCodeSnapshot,
                    null,
                    allocation.OrderRow.OrderNumber,
                    line.Description,
                    allocation.OrderRow.NetUnitPrice,
                    allocation.OrderRow.GrossUnitPrice,
                    allocation.OrderRow.ProjectCode,
                    allocation.OrderRow.OrderLineSequence)));
            else
            {
                lines.Add(NewLine(line.StockCodeSnapshot, receiptQuantity, warehouse.WarehouseCode,
                    null, null, line.YapCodeSnapshot, null, null, line.Description));
            }
        }

        var headerProjectCode = ResolveHeaderProjectCode(usedOrderRows);
        var orderDeliveryDate = usedOrderRows
            .Where(x => x.DeliveryDate.HasValue)
            .Select(x => x.DeliveryDate!.Value)
            .OrderBy(x => x)
            .FirstOrDefault();
        if (usedOrderRows.Count > 0 && orderDeliveryDate == default)
            orderDeliveryDate = header.DocumentDate == default
                ? DateTime.Now
                : header.DocumentDate.ToDateTime(TimeOnly.MinValue);
        return NewRequest(
            options.GoodsReceiptDocumentType,
            options.GoodsReceiptSeries,
            ResolveGoodsReceiptErpDocumentNo(header),
            ResolveGoodsReceiptErpDocumentNo(header),
            header.DocumentDate,
            header.ReceivedAtUtc,
            header.SupplierCodeSnapshot,
            warehouse,
            header.Description,
            lines,
            headerProjectCode,
            orderDeliveryDate == default ? null : orderDeliveryDate,
            ResolveGoodsReceiptInvoiceType(options));
    }

    internal static NetsisItemSlipInvoiceType ResolveGoodsReceiptInvoiceType(NetsisRestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.GoodsReceiptInvoiceType))
            throw new InvalidOperationException(
                $"Unsupported Netsis goods receipt invoice type: {(int)options.GoodsReceiptInvoiceType}.");

        return options.GoodsReceiptInvoiceType;
    }

    internal static decimal GoodsReceiptQuantityForErp(GoodsReceiptLine line) =>
        line.ReceivedQuantity;

    internal static string ResolveGoodsReceiptErpDocumentNo(GoodsReceiptHeader header)
    {
        var documentNo = Clean(header.ElectronicWaybillNo) ?? Clean(header.WaybillNo);
        if (documentNo is null)
            throw AppException.Conflict(
                "ERP alış irsaliyesi için normal irsaliye veya e-irsaliye/GİB numarası zorunludur.");
        return documentNo;
    }

    private async Task<NetsisItemSlipRequest> MapWarehouseTransferAsync(
        WarehouseTransferHeader header,
        WarehouseEntity sourceWarehouse,
        WarehouseEntity targetWarehouse,
        bool sendSerials,
        CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value.Rest;
        var orderContext = await BuildTransferOrderContextAsync(header, cancellationToken);
        var orderDocumentIds = orderContext.DocumentIds;
        var lines = new List<NetsisItemSlipLine>();
        var usedOrderRows = new List<ErpOrderRow>();
        foreach (var line in header.Lines.OrderBy(x => x.LineNo))
        {
            var quantity = line.ShippedQuantity;
            if (quantity <= 0) continue;
            var allocations = AllocateOrderLinkedLine(
                line.StockCodeSnapshot,
                line.YapCodeSnapshot,
                quantity,
                line.Sources
                    .Where(x => orderDocumentIds.Contains(x.WtSourceDocumentId))
                    .Select(x => new ErpLineSourceRef(
                    x.WtSourceDocumentId, x.ExternalLineId, x.ExternalStockCode, x.ExternalYapCode, x.AllocatedQuantity)),
                orderContext);
            usedOrderRows.AddRange(allocations.Select(x => x.OrderRow));
            var serials = line.Trackings.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo) && x.ShippedQuantity > 0).ToList();
            if (sendSerials && line.RequireSerial)
            {
                if (serials.Count == 0 || serials.Sum(x => x.ShippedQuantity) != quantity)
                    throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için sevk miktarıyla eşleşen seri kayıtları tamamlanmadan ERP transferi oluşturulamaz.");
                AddSerialOrderLines(
                    lines,
                    line.StockCodeSnapshot,
                    line.YapCodeSnapshot,
                    line.Description,
                    serials.Select(x => new ErpSerialPart(x.ShippedQuantity, x.SerialNo!)),
                    allocations,
                    null,
                    sourceWarehouse.WarehouseCode,
                    targetWarehouse.WarehouseCode);
            }
            else if (allocations.Count > 0)
                lines.AddRange(allocations.Select(x => NewOrderLinkedLine(
                    line.StockCodeSnapshot, x.Quantity, null, sourceWarehouse.WarehouseCode,
                    targetWarehouse.WarehouseCode, line.YapCodeSnapshot, null, line.Description, x.OrderRow)));
            else
                lines.Add(NewLine(line.StockCodeSnapshot, quantity, null, sourceWarehouse.WarehouseCode,
                    targetWarehouse.WarehouseCode, line.YapCodeSnapshot, null, null, line.Description));
        }

        return NewRequest(options.WarehouseTransferDocumentType, options.WarehouseTransferSeries,
            header.DocumentNo, header.WaybillNo ?? header.DocumentNo, header.DocumentDate, header.ShippedAtUtc,
            null, sourceWarehouse, header.Description, lines,
            usedOrderRows.Count > 0
                ? ResolveErpHeaderProjectCode(usedOrderRows)
                : NetsisItemSlipDefaults.NormalizeProjectCode(header.ProjectCode),
            ResolveErpDeliveryDate(usedOrderRows, header.DocumentDate));
    }

    private async Task<NetsisItemSlipRequest> MapShipmentAsync(
        ShipmentHeader header,
        WarehouseEntity warehouse,
        bool sendSerials,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(header.CustomerCodeSnapshot))
            throw AppException.Conflict("Sevk ERP irsaliyesi için müşteri kodu zorunludur.");

        var options = optionsAccessor.Value.Rest;
        var orderDocuments = header.SourceDocuments
            .Where(x => string.Equals(x.SourceDocumentType, "SalesOrder", StringComparison.OrdinalIgnoreCase))
            .Select(x => new ErpSourceDocumentRef(x.Id, x.ExternalDocumentNo))
            .ToList();
        var orderContext = await BuildShipmentOrderContextAsync(
            orderDocuments,
            header.BranchCode,
            header.CustomerCodeSnapshot,
            cancellationToken);
        var orderDocumentIds = orderContext.DocumentIds;
        var lines = new List<NetsisItemSlipLine>();
        var usedOrderRows = new List<ErpOrderRow>();
        foreach (var line in header.Lines.OrderBy(x => x.LineNo))
        {
            var quantity = line.ShippedQuantity;
            if (quantity <= 0) continue;
            var allocations = AllocateOrderLinkedLine(
                line.StockCodeSnapshot,
                line.YapCodeSnapshot,
                quantity,
                line.Sources
                    .Where(x => orderDocumentIds.Contains(x.ShipmentSourceDocumentId))
                    .Select(x => new ErpLineSourceRef(
                    x.ShipmentSourceDocumentId, x.ExternalLineId, x.ExternalStockCode, x.ExternalYapCode, x.AllocatedQuantity)),
                orderContext);
            usedOrderRows.AddRange(allocations.Select(x => x.OrderRow));
            var serials = line.Trackings.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo) && x.ShippedQuantity > 0).ToList();
            var serialTracked = line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial;
            if (sendSerials && serialTracked)
            {
                if (serials.Count == 0 || serials.Sum(x => x.ShippedQuantity) != quantity)
                    throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için sevk miktarıyla eşleşen seri kayıtları tamamlanmadan ERP irsaliyesi oluşturulamaz.");
                AddSerialOrderLines(
                    lines,
                    line.StockCodeSnapshot,
                    line.YapCodeSnapshot,
                    line.Description,
                    serials.Select(x => new ErpSerialPart(x.ShippedQuantity, x.SerialNo!)),
                    allocations,
                    warehouse.WarehouseCode,
                    null,
                    null);
            }
            else if (allocations.Count > 0)
                lines.AddRange(allocations.Select(x => NewOrderLinkedLine(
                    line.StockCodeSnapshot, x.Quantity, warehouse.WarehouseCode, null,
                    null, line.YapCodeSnapshot, null, line.Description, x.OrderRow)));
            else
                lines.Add(NewLine(line.StockCodeSnapshot, quantity, warehouse.WarehouseCode,
                    null, null, line.YapCodeSnapshot, null, null, line.Description));
        }

        return NewRequest(options.ShipmentDocumentType, options.ShipmentSeries,
            header.DocumentNo, header.WaybillNo ?? header.DocumentNo, header.DocumentDate, header.ShippedAtUtc,
            header.CustomerCodeSnapshot, warehouse, header.Description, lines,
            ResolveErpHeaderProjectCode(usedOrderRows), ResolveErpDeliveryDate(usedOrderRows, header.DocumentDate));
    }

    private async Task<NetsisItemSlipRequest> MapWarehouseInboundAsync(
        WarehouseInboundHeader header,
        WarehouseEntity warehouse,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(header.SupplierCodeSnapshot))
            throw AppException.Conflict("Ambar giriş ERP irsaliyesi için tedarikçi kodu zorunludur.");

        var purchaseOrderDocuments = header.SourceDocuments
            .Where(x => x.SourceSystem == WarehouseOperationSourceSystem.Netsis
                && x.SourceDocumentType == WarehouseInboundSourceDocumentType.PurchaseOrder)
            .Select(x => new ErpSourceDocumentRef(x.Id, x.ExternalDocumentNo))
            .ToList();
        var context = await BuildPurchaseOrderContextAsync(
            purchaseOrderDocuments, header.SupplierCodeSnapshot, header.BranchCode, cancellationToken);
        var orderDocumentIds = context.DocumentIds;
        var sendSerials = await SendSerialsToErpAsync(cancellationToken);
        var serials = sendSerials
            ? await unitOfWork.Repository<WarehouseInboundExecutionLine>().Query()
                .Where(x => x.Execution.GrHeaderId == header.Id
                    && x.Execution.Status == WarehouseInboundExecutionStatus.Posted
                    && x.SerialNo != null)
                .Select(x => new { x.GrLineId, x.Quantity, x.SerialNo })
                .ToListAsync(cancellationToken)
            : [];
        var lines = new List<NetsisItemSlipLine>();
        var usedOrderRows = new List<ErpOrderRow>();
        foreach (var line in header.Lines.OrderBy(x => x.LineNo))
        {
            var quantity = line.ReceivedQuantity;
            if (quantity <= 0) continue;
            var allocations = AllocateOrderLinkedLine(
                line.StockCodeSnapshot,
                line.YapCodeSnapshot,
                quantity,
                line.Sources
                    .Where(x => orderDocumentIds.Contains(x.GrSourceDocumentId))
                    .Select(x => new ErpLineSourceRef(
                    x.GrSourceDocumentId, x.ExternalLineId, x.ExternalStockCode, x.ExternalYapCode, x.AllocatedQuantity)),
                context);
            usedOrderRows.AddRange(allocations.Select(x => x.OrderRow));
            var lineSerials = serials.Where(x => x.GrLineId == line.Id).ToList();
            if (sendSerials && line.RequireSerial)
            {
                if (lineSerials.Count == 0 || lineSerials.Sum(x => x.Quantity) != quantity)
                    throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için kabul miktarıyla eşleşen seri kayıtları tamamlanmadan ERP belgesi oluşturulamaz.");
                AddSerialOrderLines(
                    lines,
                    line.StockCodeSnapshot,
                    line.YapCodeSnapshot,
                    line.Description,
                    lineSerials.Select(x => new ErpSerialPart(x.Quantity, x.SerialNo!)),
                    allocations,
                    warehouse.WarehouseCode,
                    null,
                    null);
            }
            else if (allocations.Count > 0)
                lines.AddRange(allocations.Select(x => NewOrderLinkedLine(
                    line.StockCodeSnapshot, x.Quantity, warehouse.WarehouseCode, null,
                    null, line.YapCodeSnapshot, null, line.Description, x.OrderRow)));
            else
                lines.Add(NewLine(line.StockCodeSnapshot, quantity, warehouse.WarehouseCode,
                    null, null, line.YapCodeSnapshot, null, null, line.Description));
        }

        var options = optionsAccessor.Value.Rest;
        var documentNo = ResolveWarehouseInboundErpDocumentNo(header);
        return NewRequest(
            options.GoodsReceiptDocumentType,
            options.GoodsReceiptSeries,
            documentNo,
            documentNo,
            header.DocumentDate,
            header.ReceivedAtUtc,
            header.SupplierCodeSnapshot,
            warehouse,
            header.Description,
            lines,
            ResolveErpHeaderProjectCode(usedOrderRows),
            ResolveErpDeliveryDate(usedOrderRows, header.DocumentDate));
    }

    private async Task<NetsisItemSlipRequest> MapWarehouseOutboundAsync(
        WarehouseOutboundHeader header,
        WarehouseEntity warehouse,
        bool sendSerials,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(header.CustomerCodeSnapshot))
            throw AppException.Conflict("Ambar çıkış ERP irsaliyesi için müşteri kodu zorunludur.");

        var orderDocuments = header.SourceDocuments
            .Where(x => string.Equals(x.SourceDocumentType, "SalesOrder", StringComparison.OrdinalIgnoreCase))
            .Select(x => new ErpSourceDocumentRef(x.Id, x.ExternalDocumentNo))
            .ToList();
        var context = await BuildShipmentOrderContextAsync(
            orderDocuments,
            header.BranchCode,
            header.CustomerCodeSnapshot,
            cancellationToken);
        var orderDocumentIds = context.DocumentIds;
        var lines = new List<NetsisItemSlipLine>();
        var usedOrderRows = new List<ErpOrderRow>();
        foreach (var line in header.Lines.OrderBy(x => x.LineNo))
        {
            var quantity = line.ShippedQuantity;
            if (quantity <= 0) continue;
            var allocations = AllocateOrderLinkedLine(
                line.StockCodeSnapshot,
                line.YapCodeSnapshot,
                quantity,
                line.Sources
                    .Where(x => orderDocumentIds.Contains(x.WarehouseOutboundSourceDocumentId))
                    .Select(x => new ErpLineSourceRef(
                    x.WarehouseOutboundSourceDocumentId, x.ExternalLineId, x.ExternalStockCode, x.ExternalYapCode, x.AllocatedQuantity)),
                context);
            usedOrderRows.AddRange(allocations.Select(x => x.OrderRow));
            var trackingRows = line.Trackings
                .Where(x => !string.IsNullOrWhiteSpace(x.SerialNo) && x.ShippedQuantity > 0)
                .ToList();
            var serialTracked = line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial;
            if (sendSerials && serialTracked)
            {
                if (trackingRows.Count == 0 || trackingRows.Sum(x => x.ShippedQuantity) != quantity)
                    throw AppException.Conflict(
                        $"{line.StockCodeSnapshot} için sevk miktarıyla eşleşen seri kayıtları tamamlanmadan ERP belgesi oluşturulamaz.");
                AddSerialOrderLines(
                    lines,
                    line.StockCodeSnapshot,
                    line.YapCodeSnapshot,
                    line.Description,
                    trackingRows.Select(x => new ErpSerialPart(x.ShippedQuantity, x.SerialNo!)),
                    allocations,
                    warehouse.WarehouseCode,
                    null,
                    null,
                    line.ProjectCode ?? header.ProjectCode);
            }
            else if (allocations.Count > 0)
                lines.AddRange(allocations.Select(x => NewOrderLinkedLine(
                    line.StockCodeSnapshot, x.Quantity, warehouse.WarehouseCode, null,
                    null, line.YapCodeSnapshot, null, line.Description, x.OrderRow)));
            else
                lines.Add(NewLine(line.StockCodeSnapshot, quantity, warehouse.WarehouseCode,
                    null, null, line.YapCodeSnapshot, null, null, line.Description,
                    projectCode: line.ProjectCode ?? header.ProjectCode));
        }

        var options = optionsAccessor.Value.Rest;
        return NewRequest(
            options.ShipmentDocumentType,
            options.ShipmentSeries,
            header.DocumentNo,
            header.WaybillNo ?? header.DocumentNo,
            header.DocumentDate,
            header.ShippedAtUtc,
            header.CustomerCodeSnapshot,
            warehouse,
            header.Description,
            lines,
            usedOrderRows.Count > 0 ? ResolveErpHeaderProjectCode(usedOrderRows) : header.ProjectCode,
            ResolveErpDeliveryDate(usedOrderRows, header.DocumentDate));
    }

    internal static string ResolveWarehouseInboundErpDocumentNo(WarehouseInboundHeader header)
    {
        var documentNo = Clean(header.ElectronicWaybillNo) ?? Clean(header.WaybillNo);
        if (documentNo is null)
            throw AppException.Conflict(
                "ERP ambar giriş belgesi için normal irsaliye veya e-irsaliye/GİB numarası zorunludur.");
        return documentNo;
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
        List<NetsisItemSlipLine> lines,
        string? projectCode = null,
        DateTime? orderDeliveryDate = null,
        NetsisItemSlipInvoiceType invoiceType = NetsisItemSlipInvoiceType.DomesticClosed)
    {
        if (lines.Count == 0) throw AppException.Conflict("ERP belgesi için pozitif miktarlı en az bir kalem gerekir.");
        var now = DateTime.Now;
        var resolvedDocumentDate = documentDate == default
            ? now
            : documentDate.ToDateTime(TimeOnly.MinValue);
        var actual = actualAtUtc is null || actualAtUtc == default
            ? now
            : actualAtUtc.Value.LocalDateTime;
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
                Tarih = resolvedDocumentDate,
                FiyatTarihi = resolvedDocumentDate.ToString("dd.MM.yyyy"),
                SiparisTeslimTarihi = orderDeliveryDate?.ToString("dd.MM.yyyy"),
                FiiliTarih = actual,
                ProjeKodu = NetsisItemSlipDefaults.NormalizeProjectCode(projectCode),
                Tip = documentType,
                Tipi = invoiceType,
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
        string? description,
        decimal netUnitPrice = 0,
        decimal grossUnitPrice = 0,
        string? projectCode = null,
        int orderLineSequence = 0) => new()
        {
            StokKodu = stockCode,
            Miktar = quantity,
            DepoKodu = warehouseCode,
            CikisDepoKodu = sourceWarehouseCode,
            GirisDepoKodu = targetWarehouseCode,
            ConfigurationCode = yapCode,
            SeriNo = serialNo,
            NetFiyat = netUnitPrice,
            BrutFiyat = grossUnitPrice,
            SiparisNumarasi = Clean(orderNo) ?? string.Empty,
            SiparisKontrol = Clean(orderNo) is null ? 0 : orderLineSequence,
            Aciklama = description,
            ProjeKodu = NetsisItemSlipDefaults.NormalizeProjectCode(projectCode)
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

    private async Task SetSourceHeaderStatusAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        ErpIntegrationStatus status,
        CancellationToken cancellationToken)
    {
        switch (sourceType)
        {
            case ErpPostingSourceType.GoodsReceipt:
            {
                var entity = await unitOfWork.Repository<GoodsReceiptHeader>()
                    .FindByIdAsync(sourceEntityId, true, cancellationToken)
                    ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
                entity.ErpIntegrationStatus = status;
                unitOfWork.Repository<GoodsReceiptHeader>().Update(entity);
                break;
            }
            case ErpPostingSourceType.WarehouseTransfer:
            {
                var entity = await unitOfWork.Repository<WarehouseTransferHeader>()
                    .FindByIdAsync(sourceEntityId, true, cancellationToken)
                    ?? throw AppException.NotFound("Transfer kaydı bulunamadı.");
                entity.ErpIntegrationStatus = status;
                unitOfWork.Repository<WarehouseTransferHeader>().Update(entity);
                break;
            }
            case ErpPostingSourceType.Shipment:
            {
                var entity = await unitOfWork.Repository<ShipmentHeader>()
                    .FindByIdAsync(sourceEntityId, true, cancellationToken)
                    ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");
                entity.ErpIntegrationStatus = status;
                unitOfWork.Repository<ShipmentHeader>().Update(entity);
                break;
            }
            case ErpPostingSourceType.WarehouseInbound:
            {
                var entity = await unitOfWork.Repository<WarehouseInboundHeader>()
                    .FindByIdAsync(sourceEntityId, true, cancellationToken)
                    ?? throw AppException.NotFound("Ambar giriş kaydı bulunamadı.");
                entity.ErpIntegrationStatus = status;
                unitOfWork.Repository<WarehouseInboundHeader>().Update(entity);
                break;
            }
            case ErpPostingSourceType.WarehouseOutbound:
            {
                var entity = await unitOfWork.Repository<WarehouseOutboundHeader>()
                    .FindByIdAsync(sourceEntityId, true, cancellationToken)
                    ?? throw AppException.NotFound("Ambar çıkış kaydı bulunamadı.");
                entity.ErpIntegrationStatus = status;
                unitOfWork.Repository<WarehouseOutboundHeader>().Update(entity);
                break;
            }
            default:
                throw AppException.BadRequest("Desteklenmeyen ERP kaynak tipi.");
        }
    }

    private static void ValidateGoodsReceiptGate(GoodsReceiptHeader header)
    {
        var qualitySources = header.Lines.Where(x => x.RequireQualityControl)
            .Select(x => x.QualityRoutingSource).ToArray();
        var hasManualQualityPlan = qualitySources.Contains(GoodsReceiptQualityRoutingSource.ManualReceipt);
        var hasRuleBasedQualityPlan = qualitySources.Any(x => x is
            GoodsReceiptQualityRoutingSource.StockRule
            or GoodsReceiptQualityRoutingSource.StockGroupRule
            or GoodsReceiptQualityRoutingSource.GlobalDefault);
        if (header.RequireQualityControl && qualitySources.Length > 0
            && !hasManualQualityPlan && !hasRuleBasedQualityPlan)
            hasRuleBasedQualityPlan = true;
        if (!GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
                header.Status,
                header.ApprovalStatus,
                header.QualityStatus,
                header.ErpPostingPolicy,
                header.ErpQualityGatePolicy,
                hasRuleBasedQualityPlan,
                hasManualQualityPlan))
            throw AppException.Conflict("Mal kabul onay veya kalite kapısı tamamlanmadan ERP irsaliyesi oluşturulamaz.");

        if (header.Status is not (WarehouseOperationStatus.Processed or WarehouseOperationStatus.Completed))
            throw AppException.Conflict("ERP mal kabul irsaliyesi için fiziksel kabulün tamamlanmış olması gerekir.");
        if (header.ErpPostingPolicy is GoodsReceiptErpPostingPolicy.AfterReceiptApproval
            or GoodsReceiptErpPostingPolicy.AfterAllApprovals
            && header.ApprovalStatus is not (OperationApprovalStatus.NotRequired or OperationApprovalStatus.Approved))
            throw AppException.Conflict("Mal kabul onayı tamamlanmadan ERP irsaliyesi oluşturulamaz.");
        if (header.ErpPostingPolicy is GoodsReceiptErpPostingPolicy.AfterQualityApproval
            or GoodsReceiptErpPostingPolicy.AfterAllApprovals
            && header.QualityStatus is not (OperationQualityStatus.NotRequired
                or OperationQualityStatus.Passed
                or OperationQualityStatus.Failed))
            throw AppException.Conflict("Kalite kararı tamamlanmadan ERP irsaliyesi oluşturulamaz.");
    }

    private static GoodsReceiptOrderSourceLine ResolveGoodsReceiptOrderRow(
        GoodsReceiptLineSource source,
        IReadOnlyDictionary<long, GoodsReceiptSourceDocument> purchaseOrderDocuments,
        IReadOnlyDictionary<(string OrderNumber, int OrderId), GoodsReceiptOrderSourceLine> orderRows)
    {
        var document = purchaseOrderDocuments[source.GrSourceDocumentId];
        if (!int.TryParse(source.ExternalLineId, out var orderId))
            throw AppException.Conflict(
                $"{source.ExternalStockCode} sipariş satırı kimliği Netsis bağlantısı için geçersizdir.");

        var key = (document.ExternalDocumentNo.Trim().ToUpperInvariant(), orderId);
        if (orderRows.TryGetValue(key, out var orderRow))
        {
            if (orderRow.OrderLineSequence <= 0)
                throw AppException.Conflict(
                    $"{document.ExternalDocumentNo} / {source.ExternalStockCode} Netsis sipariş satırının SIRA bilgisi geçersizdir.");
            return orderRow;
        }

        throw AppException.Conflict(
            $"{document.ExternalDocumentNo} / {source.ExternalStockCode} sipariş satırı Netsis'te doğrulanamadı. " +
            "İrsaliye sipariş bağlantısı kopuk gönderilmedi.");
    }

    private static Dictionary<string, List<GoodsReceiptOrderAllocationState>> BuildGoodsReceiptOrderAllocationQueues(
        GoodsReceiptHeader header,
        IReadOnlyDictionary<long, GoodsReceiptSourceDocument> purchaseOrderDocuments,
        IReadOnlyDictionary<(string OrderNumber, int OrderId), GoodsReceiptOrderSourceLine> orderRows)
    {
        return header.Lines
            .SelectMany(line => line.Sources)
            .Where(source => purchaseOrderDocuments.ContainsKey(source.GrSourceDocumentId)
                && source.AllocatedQuantity > 0)
            .Select(source => new
            {
                Source = source,
                OrderRow = ResolveGoodsReceiptOrderRow(source, purchaseOrderDocuments, orderRows)
            })
            .GroupBy(
                x => new
                {
                    Key = OrderAllocationKey(x.Source.ExternalStockCode, x.Source.ExternalYapCode),
                    x.OrderRow.OrderNumber,
                    x.OrderRow.OrderId
                })
            .Select(group => new GoodsReceiptOrderAllocationState(
                group.Key.Key,
                group.First().OrderRow,
                Math.Min(
                    group.Sum(x => x.Source.AllocatedQuantity),
                    group.First().OrderRow.RemainingQuantity)))
            .GroupBy(x => x.Key)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(x => x.OrderRow.OrderDate ?? DateTime.MaxValue)
                    .ThenBy(x => x.OrderRow.OrderNumber, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.OrderRow.OrderLineSequence)
                    .ThenBy(x => x.OrderRow.DeliveryDate ?? DateTime.MaxValue)
                    .ThenBy(x => x.OrderRow.OrderId)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<GoodsReceiptOrderAllocation> AllocateOrderQuantity(
        IList<GoodsReceiptOrderAllocationState> queue,
        decimal quantity,
        string stockCode)
    {
        var remaining = quantity;
        var result = new List<GoodsReceiptOrderAllocation>();
        foreach (var state in queue)
        {
            if (remaining <= 0)
                break;
            if (state.RemainingQuantity <= 0)
                continue;

            var allocated = Math.Min(remaining, state.RemainingQuantity);
            result.Add(new GoodsReceiptOrderAllocation(state.OrderRow, allocated));
            state.RemainingQuantity -= allocated;
            remaining -= allocated;
        }

        if (remaining > 0)
            throw AppException.Conflict(
                $"{stockCode} için kabul edilen {quantity} miktarın {remaining} kadarı seçili Netsis sipariş satırlarına bağlanamadı.");
        return result;
    }

    private static void AddSerialGoodsReceiptLines(
        ICollection<NetsisItemSlipLine> lines,
        GoodsReceiptLine line,
        IEnumerable<GoodsReceiptSerialPart> serialParts,
        IReadOnlyList<GoodsReceiptOrderAllocation> allocations,
        int warehouseCode)
    {
        if (allocations.Count == 0)
        {
            foreach (var serial in serialParts)
                lines.Add(NewLine(
                    line.StockCodeSnapshot, serial.Quantity, warehouseCode, null, null,
                    line.YapCodeSnapshot, serial.SerialNo, null, line.Description));
            return;
        }

        var allocationIndex = 0;
        var allocationRemaining = allocations[0].Quantity;
        foreach (var serial in serialParts)
        {
            var serialRemaining = serial.Quantity;
            while (serialRemaining > 0)
            {
                var allocation = allocations[allocationIndex];
                var quantity = Math.Min(serialRemaining, allocationRemaining);
                lines.Add(NewLine(
                    line.StockCodeSnapshot,
                    quantity,
                    warehouseCode,
                    null,
                    null,
                    line.YapCodeSnapshot,
                    serial.SerialNo,
                    allocation.OrderRow.OrderNumber,
                    line.Description,
                    allocation.OrderRow.NetUnitPrice,
                    allocation.OrderRow.GrossUnitPrice,
                    allocation.OrderRow.ProjectCode,
                    allocation.OrderRow.OrderLineSequence));
                serialRemaining -= quantity;
                allocationRemaining -= quantity;
                if (allocationRemaining == 0 && allocationIndex + 1 < allocations.Count)
                {
                    allocationIndex++;
                    allocationRemaining = allocations[allocationIndex].Quantity;
                }
            }
        }
    }

    private static string OrderAllocationKey(string stockCode, string? yapCode) =>
        $"{stockCode.Trim().ToUpperInvariant()}\u001F{Clean(yapCode)?.ToUpperInvariant() ?? string.Empty}";

    internal static string ResolveHeaderProjectCode(IEnumerable<GoodsReceiptOrderSourceLine> orderRows)
    {
        var projectCodes = orderRows
            .Select(x => Clean(x.ProjectCode))
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return projectCodes.Count == 1
            ? projectCodes[0]
            : NetsisItemSlipDefaults.DefaultProjectCode;
    }

    internal sealed record GoodsReceiptOrderAllocation(
        GoodsReceiptOrderSourceLine OrderRow,
        decimal Quantity);

    internal sealed class GoodsReceiptOrderAllocationState(
        string key,
        GoodsReceiptOrderSourceLine orderRow,
        decimal remainingQuantity)
    {
        public string Key { get; } = key;
        public GoodsReceiptOrderSourceLine OrderRow { get; } = orderRow;
        public decimal RemainingQuantity { get; set; } = remainingQuantity;
    }

    private sealed record GoodsReceiptSerialPart(decimal Quantity, string SerialNo);

    private async Task<ErpOrderContext> BuildPurchaseOrderContextAsync(
        IEnumerable<ErpSourceDocumentRef> sourceDocuments,
        string customerCode,
        string branchCode,
        CancellationToken cancellationToken)
    {
        var documents = sourceDocuments
            .Where(x => !string.IsNullOrWhiteSpace(x.ExternalDocumentNo))
            .ToDictionary(x => x.Id);
        if (documents.Count == 0) return ErpOrderContext.Empty;
        var rows = await goodsReceiptOrderSource.GetOpenLinesAsync(
            string.Join(',', documents.Values.Select(x => x.ExternalDocumentNo).Distinct(StringComparer.OrdinalIgnoreCase)),
            customerCode,
            branchCode,
            cancellationToken);
        return new ErpOrderContext(
            documents,
            rows.Select(x => new ErpOrderRow(
                x.OrderNumber,
                x.OrderId.ToString(),
                x.OrderLineSequence,
                x.StockCode ?? string.Empty,
                x.YapCode,
                x.CustomerCode,
                x.ProjectCode,
                x.OrderDate,
                x.DeliveryDate,
                x.NetUnitPrice,
                x.GrossUnitPrice,
                x.RemainingQuantity)));
    }

    private async Task<ErpOrderContext> BuildTransferOrderContextAsync(
        WarehouseTransferHeader header,
        CancellationToken cancellationToken)
    {
        var documents = header.SourceDocuments
            .Where(x => x.SourceSystem == WarehouseOperationSourceSystem.Netsis)
            .Select(x => new ErpSourceDocumentRef(x.Id, x.ExternalDocumentNo))
            .Where(x => !string.IsNullOrWhiteSpace(x.ExternalDocumentNo))
            .ToDictionary(x => x.Id);
        if (documents.Count == 0) return ErpOrderContext.Empty;
        var rows = await netsisReadService.GetWarehouseTransferOpenOrderLinesAsync(
            string.Join(',', documents.Values.Select(x => x.ExternalDocumentNo).Distinct(StringComparer.OrdinalIgnoreCase)),
            header.BranchCode,
            cancellationToken);
        return new ErpOrderContext(
            documents,
            rows.Select(x => new ErpOrderRow(
                x.OrderNumber,
                x.OrderId.ToString(),
                x.OrderLineSequence,
                x.StockCode ?? string.Empty,
                x.YapCode,
                x.CustomerCode,
                x.ProjectCode,
                x.OrderDate,
                x.DeliveryDate,
                x.NetUnitPrice ?? 0,
                x.GrossUnitPrice ?? 0,
                x.RemainingQuantity ?? 0)));
    }

    private async Task<ErpOrderContext> BuildShipmentOrderContextAsync(
        IEnumerable<ErpSourceDocumentRef> sourceDocuments,
        string branchCode,
        string customerCode,
        CancellationToken cancellationToken)
    {
        var documents = sourceDocuments
            .Where(x => !string.IsNullOrWhiteSpace(x.ExternalDocumentNo))
            .ToDictionary(x => x.Id);
        if (documents.Count == 0) return ErpOrderContext.Empty;
        var rows = await netsisReadService.GetShipmentOpenOrderLinesAsync(
            string.Join(',', documents.Values.Select(x => x.ExternalDocumentNo).Distinct(StringComparer.OrdinalIgnoreCase)),
            branchCode,
            cancellationToken);
        var invalidCustomer = rows.FirstOrDefault(x =>
            !string.Equals(Clean(x.CustomerCode), Clean(customerCode), StringComparison.OrdinalIgnoreCase));
        if (invalidCustomer is not null)
            throw AppException.Conflict(
                $"{invalidCustomer.OrderNumber} numaralı sipariş ERP belgesindeki {customerCode} carisine ait değildir.");
        return new ErpOrderContext(
            documents,
            rows.Select(x => new ErpOrderRow(
                x.OrderNumber,
                x.OrderId.ToString(),
                x.OrderLineSequence,
                x.StockCode ?? string.Empty,
                x.YapCode,
                x.CustomerCode,
                x.ProjectCode,
                x.OrderDate,
                x.DeliveryDate,
                x.NetUnitPrice ?? 0,
                x.GrossUnitPrice ?? 0,
                x.RemainingQuantity ?? 0)));
    }

    private static IReadOnlyList<ErpOrderAllocation> AllocateOrderLinkedLine(
        string stockCode,
        string? yapCode,
        decimal quantity,
        IEnumerable<ErpLineSourceRef> sourceRows,
        ErpOrderContext context)
    {
        var sources = sourceRows.Where(x => x.AllocatedQuantity > 0).ToList();
        if (sources.Count == 0) return [];
        if (context.Documents.Count == 0)
            throw AppException.Conflict(
                $"{stockCode} için Netsis sipariş kaynağı kayıtlı ancak canlı sipariş bağlamı kurulamadı.");

        var candidates = sources.Select(source =>
        {
            if (!context.Documents.TryGetValue(source.SourceDocumentId, out var document))
                throw AppException.Conflict($"{stockCode} sipariş kaynak belgesi bulunamadı.");
            var row = context.Resolve(document.ExternalDocumentNo, source.ExternalLineId);
            if (!string.Equals(
                    OrderAllocationKey(stockCode, yapCode),
                    OrderAllocationKey(row.StockCode, row.YapCode),
                    StringComparison.OrdinalIgnoreCase))
                throw AppException.Conflict(
                    $"{document.ExternalDocumentNo}/{source.ExternalLineId} Netsis sipariş satırı stok veya yapı koduyla eşleşmiyor.");
            return new ErpAllocationCandidate(row, source.AllocatedQuantity);
        })
        .OrderBy(x => x.Row.OrderDate ?? DateTime.MaxValue)
        .ThenBy(x => x.Row.OrderNumber, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Row.OrderLineSequence)
        .ThenBy(x => x.Row.DeliveryDate ?? DateTime.MaxValue)
        .ToList();

        var remaining = quantity;
        var allocations = new List<ErpOrderAllocation>();
        foreach (var candidate in candidates)
        {
            if (remaining <= 0) break;
            var liveRemaining = context.RemainingByRow.GetValueOrDefault(candidate.Row.Key);
            var available = Math.Min(candidate.AllocatedQuantity, liveRemaining);
            if (available <= 0) continue;
            var allocated = Math.Min(remaining, available);
            allocations.Add(new ErpOrderAllocation(candidate.Row, allocated));
            context.RemainingByRow[candidate.Row.Key] = liveRemaining - allocated;
            remaining -= allocated;
        }

        if (remaining > 0)
            throw AppException.Conflict(
                $"{stockCode} için işlenen {quantity} miktarın {remaining} kadarı güncel Netsis sipariş satırlarına bağlanamadı. " +
                "Bağlantısız ERP belgesi gönderilmedi.");
        return allocations;
    }

    private static NetsisItemSlipLine NewOrderLinkedLine(
        string stockCode,
        decimal quantity,
        int? warehouseCode,
        int? sourceWarehouseCode,
        int? targetWarehouseCode,
        string? yapCode,
        string? serialNo,
        string? description,
        ErpOrderRow orderRow) =>
        NewLine(
            stockCode,
            quantity,
            warehouseCode,
            sourceWarehouseCode,
            targetWarehouseCode,
            yapCode,
            serialNo,
            orderRow.OrderNumber,
            description,
            orderRow.NetUnitPrice,
            orderRow.GrossUnitPrice,
            orderRow.ProjectCode,
            orderRow.OrderLineSequence);

    private static void AddSerialOrderLines(
        ICollection<NetsisItemSlipLine> target,
        string stockCode,
        string? yapCode,
        string? description,
        IEnumerable<ErpSerialPart> serialParts,
        IReadOnlyList<ErpOrderAllocation> allocations,
        int? warehouseCode,
        int? sourceWarehouseCode,
        int? targetWarehouseCode,
        string? fallbackProjectCode = null)
    {
        if (allocations.Count == 0)
        {
            foreach (var serial in serialParts)
                target.Add(NewLine(
                    stockCode, serial.Quantity, warehouseCode, sourceWarehouseCode, targetWarehouseCode,
                    yapCode, serial.SerialNo, null, description, projectCode: fallbackProjectCode));
            return;
        }

        var allocationIndex = 0;
        var allocationRemaining = allocations[0].Quantity;
        foreach (var serial in serialParts)
        {
            var serialRemaining = serial.Quantity;
            while (serialRemaining > 0)
            {
                var allocation = allocations[allocationIndex];
                var part = Math.Min(serialRemaining, allocationRemaining);
                target.Add(NewOrderLinkedLine(
                    stockCode, part, warehouseCode, sourceWarehouseCode, targetWarehouseCode,
                    yapCode, serial.SerialNo, description, allocation.OrderRow));
                serialRemaining -= part;
                allocationRemaining -= part;
                if (allocationRemaining == 0 && allocationIndex + 1 < allocations.Count)
                {
                    allocationIndex++;
                    allocationRemaining = allocations[allocationIndex].Quantity;
                }
            }
        }
    }

    private static string ResolveErpHeaderProjectCode(IEnumerable<ErpOrderRow> rows)
    {
        var values = rows.Select(x => Clean(x.ProjectCode))
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count == 1 ? values[0] : NetsisItemSlipDefaults.DefaultProjectCode;
    }

    private static DateTime? ResolveErpDeliveryDate(IEnumerable<ErpOrderRow> rows, DateOnly fallback)
    {
        var list = rows.ToList();
        if (list.Count == 0) return null;
        return list.Where(x => x.DeliveryDate.HasValue)
            .Select(x => x.DeliveryDate!.Value)
            .OrderBy(x => x)
            .Cast<DateTime?>()
            .FirstOrDefault()
            ?? (fallback == default ? DateTime.Now : fallback.ToDateTime(TimeOnly.MinValue));
    }

    private sealed record ErpSourceDocumentRef(long Id, string ExternalDocumentNo);
    private sealed record ErpLineSourceRef(
        long SourceDocumentId,
        string ExternalLineId,
        string ExternalStockCode,
        string? ExternalYapCode,
        decimal AllocatedQuantity);
    private sealed record ErpSerialPart(decimal Quantity, string SerialNo);
    private sealed record ErpOrderAllocation(ErpOrderRow OrderRow, decimal Quantity);
    private sealed record ErpAllocationCandidate(ErpOrderRow Row, decimal AllocatedQuantity);
    private sealed record ErpOrderRow(
        string OrderNumber,
        string ExternalLineId,
        int OrderLineSequence,
        string StockCode,
        string? YapCode,
        string? CustomerCode,
        string? ProjectCode,
        DateTime? OrderDate,
        DateTime? DeliveryDate,
        decimal NetUnitPrice,
        decimal GrossUnitPrice,
        decimal RemainingQuantity)
    {
        public string Key => $"{OrderNumber.Trim().ToUpperInvariant()}\u001F{ExternalLineId.Trim().ToUpperInvariant()}";
    }

    private sealed class ErpOrderContext
    {
        public static ErpOrderContext Empty { get; } =
            new(new Dictionary<long, ErpSourceDocumentRef>(), []);
        public IReadOnlyDictionary<long, ErpSourceDocumentRef> Documents { get; }
        public IReadOnlySet<long> DocumentIds { get; }
        private IReadOnlyDictionary<string, ErpOrderRow> RowsByKey { get; }
        public Dictionary<string, decimal> RemainingByRow { get; }

        public ErpOrderContext(
            IReadOnlyDictionary<long, ErpSourceDocumentRef> documents,
            IEnumerable<ErpOrderRow> rows)
        {
            var materializedRows = rows.ToList();
            Documents = documents;
            DocumentIds = documents.Keys.ToHashSet();
            RowsByKey = materializedRows.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
            RemainingByRow = materializedRows.ToDictionary(
                x => x.Key,
                x => x.RemainingQuantity,
                StringComparer.OrdinalIgnoreCase);
        }

        public ErpOrderRow Resolve(string orderNumber, string externalLineId)
        {
            var key = $"{orderNumber.Trim().ToUpperInvariant()}\u001F{externalLineId.Trim().ToUpperInvariant()}";
            if (RowsByKey.TryGetValue(key, out var row) && row.OrderLineSequence > 0)
                return row;
            throw AppException.Conflict(
                $"{orderNumber}/{externalLineId} sipariş satırı Netsis'te açık ve geçerli bir SIPKONT satırı olarak doğrulanamadı.");
        }
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

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string TraceId() =>
        Activity.Current?.TraceId.ToString()
        ?? httpContextAccessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString("N");

    private static ErpPostingResult ToResult(ErpPostingRecord x) => new(
        x.Id, x.SourceType, x.SourceEntityId, x.SourceDocumentNo, x.Status, x.AttemptCount,
        x.ErpDocumentNo, x.ErpWaybillNo, x.ErpRecordNo, x.ErpReferenceNo,
        x.LastErrorCode, x.LastErrorMessage, x.CompletedAtUtc);
}
