using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Shipping.Application;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed class ErpCancellationService(
    IUnitOfWork unitOfWork,
    INetsisRestClient netsisClient,
    IOptions<NetsisOptions> optionsAccessor,
    IServiceScopeFactory scopeFactory,
    IAuditLogWriter audit,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ErpCancellationService> logger) : IErpCancellationService
{
    private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web);
    private IGenericRepository<ErpPostingRecord> Postings => unitOfWork.Repository<ErpPostingRecord>();
    private IGenericRepository<ErpCancellationRecord> Cancellations => unitOfWork.Repository<ErpCancellationRecord>();
    private IGenericRepository<ErpCancellationAttempt> Attempts => unitOfWork.Repository<ErpCancellationAttempt>();

    public async Task<ErpCancellationResult> CancelAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        CancelErpDocumentRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        ValidateRequest(sourceEntityId, request.IdempotencyKey, request.Reason);
        var reason = request.Reason.Trim();
        var requestHash = Hash(new { sourceType, sourceEntityId, request.IdempotencyKey, Reason = reason });
        var posting = await LoadPostingAsync(sourceType, sourceEntityId, true, cancellationToken);
        var deleteRequest = await ResolveDeleteRequestAsync(posting, cancellationToken);
        var erpDeleteId = deleteRequest.ToProviderId();
        var erpRecordId = ResolveErpRecordIdOrZero(posting);
        var cancellation = await Cancellations.Query(true)
            .SingleOrDefaultAsync(x => x.ErpPostingRecordId == posting.Id, cancellationToken);

        if (cancellation is null)
        {
            if (posting.Status != ErpPostingStatus.Succeeded)
                throw AppException.Conflict("Yalnızca ERP'ye başarıyla aktarılmış belgeler koordineli olarak iptal edilebilir.");
            await ValidateSourceAsync(sourceType, sourceEntityId, cancellationToken);
            cancellation = new ErpCancellationRecord
            {
                BranchCode = posting.BranchCode,
                ErpPostingRecordId = posting.Id,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = requestHash,
                Reason = reason,
                Status = ErpCancellationStatus.Pending,
                CreatedBy = userId,
                TraceId = TraceId()
            };
            await Cancellations.AddAsync(cancellation, cancellationToken);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "ERP iptal kilidi alınamadı SourceType={SourceType} SourceId={SourceId}", sourceType, sourceEntityId);
                throw AppException.Conflict("Bu belge için eş zamanlı bir ERP iptali başlatıldı.");
            }
        }
        else
        {
            EnsureSameRequest(cancellation, request.IdempotencyKey, requestHash);
            if (cancellation.Status == ErpCancellationStatus.Succeeded)
                return ToResult(cancellation, posting, erpRecordId);
            if (cancellation.Status == ErpCancellationStatus.Processing)
                throw AppException.Conflict("Bu belge için ERP iptali başka bir kullanıcı veya iş tarafından yürütülüyor.");
            if (cancellation.Status == ErpCancellationStatus.CommitUncertain)
                throw AppException.Conflict("ERP silme sonucu belirsiz. Netsis kontrol edilmeden yerel ters hareket veya yeniden silme yapılamaz.");
            if (cancellation.Status is ErpCancellationStatus.ErpDeletionConfirmed
                or ErpCancellationStatus.CompensationRequired)
                return await CompleteLocalReversalAsync(
                    cancellation, posting, erpRecordId, erpDeleteId, userId, cancellationToken);
            await ValidateSourceAsync(sourceType, sourceEntityId, cancellationToken);
        }

        cancellation.Status = ErpCancellationStatus.Processing;
        cancellation.AttemptCount++;
        cancellation.StartedAtUtc = DateTimeOffset.UtcNow;
        cancellation.CompletedAtUtc = null;
        cancellation.LastErrorCode = null;
        cancellation.LastErrorMessage = null;
        cancellation.TraceId = TraceId();
        Cancellations.Update(cancellation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var startedAt = DateTimeOffset.UtcNow;
        NetsisCallResult<NetsisDeleteItemSlipResponse> call;
        try
        {
            call = await netsisClient.DeleteItemSlipAsync(deleteRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await MarkClientCancellationUncertainAsync(cancellation, erpDeleteId, userId);
            throw;
        }
        catch (Exception ex)
        {
            call = new(false, false, false, null, 0, null, null, "ERP_DELETE_PRE_SEND_FAILURE", ex.Message);
        }

        var succeeded = call.TransportSucceeded && call.BusinessSucceeded;
        var requiresReconciliation = call.CommitUncertain || call.HttpStatusCode == StatusCodes.Status404NotFound;
        cancellation.Status = succeeded
            ? ErpCancellationStatus.ErpDeletionConfirmed
            : requiresReconciliation ? ErpCancellationStatus.CommitUncertain : ErpCancellationStatus.Failed;
        cancellation.ErpDeletedAtUtc = succeeded ? DateTimeOffset.UtcNow : null;
        cancellation.CompletedAtUtc = succeeded ? null : DateTimeOffset.UtcNow;
        cancellation.LastHttpStatusCode = call.HttpStatusCode;
        cancellation.LastErrorCode = succeeded
            ? null
            : call.HttpStatusCode == StatusCodes.Status404NotFound
                ? "ERP_DELETE_NOT_FOUND_RECONCILIATION_REQUIRED"
                : call.ErrorCode;
        cancellation.LastErrorMessage = succeeded
            ? null
            : call.HttpStatusCode == StatusCodes.Status404NotFound
                ? "ERP kaydı silme sırasında bulunamadı. Yanlış ortam veya önceden silinmiş belge riski nedeniyle WMS ters hareketi manuel mutabakata kadar durduruldu."
                : call.ErrorMessage;
        Cancellations.Update(cancellation);
        await Attempts.AddAsync(new ErpCancellationAttempt
        {
            BranchCode = posting.BranchCode,
            ErpCancellationRecordId = cancellation.Id,
            AttemptNo = cancellation.AttemptCount,
            Operation = $"{sourceType}.DeleteItemSlip",
            Endpoint = $"{optionsAccessor.Value.Rest.ItemSlipsPath.TrimEnd('/')}/{erpDeleteId}",
            HttpStatusCode = call.HttpStatusCode,
            IsSuccessful = succeeded,
            CommitUncertain = requiresReconciliation,
            DurationMs = call.DurationMs,
            ErrorCode = cancellation.LastErrorCode,
            ErrorMessage = cancellation.LastErrorMessage,
            ProviderResponse = call.RawResponse,
            TraceId = cancellation.TraceId,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = userId
        }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        if (!succeeded)
            return ToResult(cancellation, posting, erpRecordId);

        return await CompleteLocalReversalAsync(
            cancellation, posting, erpRecordId, erpDeleteId, userId, cancellationToken);
    }

    public async Task<ErpCancellationResult> GetAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        CancellationToken cancellationToken)
    {
        var posting = await LoadPostingAsync(sourceType, sourceEntityId, false, cancellationToken);
        var cancellation = await Cancellations.FirstOrDefaultAsync(
            x => x.ErpPostingRecordId == posting.Id,
            cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("ERP iptal kaydı bulunamadı.");
        return ToResult(cancellation, posting, ResolveErpRecordIdOrZero(posting));
    }

    public async Task<ErpCancellationResult> ReconcileAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        ReconcileErpCancellationRequest request,
        long userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw AppException.BadRequest("Mutabakat açıklaması en az 5 karakter olmalıdır.");

        var posting = await LoadPostingAsync(sourceType, sourceEntityId, true, cancellationToken);
        var erpRecordId = ResolveErpRecordIdOrZero(posting);
        var erpDeleteId = (await ResolveDeleteRequestAsync(posting, cancellationToken)).ToProviderId();
        var cancellation = await Cancellations.Query(true)
            .SingleOrDefaultAsync(x => x.ErpPostingRecordId == posting.Id, cancellationToken)
            ?? throw AppException.NotFound("ERP iptal kaydı bulunamadı.");
        if (cancellation.Status != ErpCancellationStatus.CommitUncertain)
            throw AppException.Conflict("Yalnızca sonucu belirsiz ERP iptalleri manuel mutabakata alınabilir.");

        cancellation.AttemptCount++;
        cancellation.LastHttpStatusCode = null;
        cancellation.LastErrorCode = request.ErpDocumentExists ? "MANUAL_RECONCILIATION_DOCUMENT_EXISTS" : null;
        cancellation.LastErrorMessage = request.ErpDocumentExists ? request.Reason.Trim() : null;
        cancellation.Status = request.ErpDocumentExists
            ? ErpCancellationStatus.Failed
            : ErpCancellationStatus.ErpDeletionConfirmed;
        cancellation.ErpDeletedAtUtc = request.ErpDocumentExists ? null : DateTimeOffset.UtcNow;
        cancellation.CompletedAtUtc = request.ErpDocumentExists ? DateTimeOffset.UtcNow : null;
        Cancellations.Update(cancellation);
        await Attempts.AddAsync(new ErpCancellationAttempt
        {
            BranchCode = posting.BranchCode,
            ErpCancellationRecordId = cancellation.Id,
            AttemptNo = cancellation.AttemptCount,
            Operation = $"{sourceType}.ManualCancellationReconciliation",
            HttpMethod = "MANUAL",
            Endpoint = "Netsis/ManualCancellationReconciliation",
            IsSuccessful = !request.ErpDocumentExists,
            ErrorCode = request.ErpDocumentExists ? "ERP_DOCUMENT_STILL_EXISTS" : null,
            ErrorMessage = request.Reason.Trim(),
            TraceId = TraceId(),
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = userId
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (request.ErpDocumentExists)
            return ToResult(cancellation, posting, erpRecordId);

        return await CompleteLocalReversalAsync(
            cancellation, posting, erpRecordId, erpDeleteId, userId, cancellationToken);
    }

    private async Task<ErpCancellationResult> CompleteLocalReversalAsync(
        ErpCancellationRecord cancellation,
        ErpPostingRecord posting,
        long erpRecordId,
        string erpDeleteId,
        long userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await SetSourceErpStatusAsync(
                posting.SourceType, posting.SourceEntityId, ErpIntegrationStatus.Cancelled, cancellationToken);

            await using var scope = scopeFactory.CreateAsyncScope();
            switch (posting.SourceType)
            {
                case ErpPostingSourceType.GoodsReceipt:
                {
                    var scopedUow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var header = await scopedUow.Repository<GoodsReceiptHeader>().FindByIdAsync(
                        posting.SourceEntityId, cancellationToken: cancellationToken)
                        ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
                    await scope.ServiceProvider.GetRequiredService<IGoodsReceiptLifecycleService>()
                        .CancelAfterErpDeletionAsync(
                            posting.SourceEntityId,
                            new(cancellation.IdempotencyKey, cancellation.Reason, Convert.ToBase64String(header.RowVersion)),
                            userId,
                            cancellationToken);
                    break;
                }
                case ErpPostingSourceType.WarehouseTransfer:
                    await scope.ServiceProvider.GetRequiredService<IWarehouseTransferOperationService>()
                        .CancelAfterErpDeletionAsync(
                            posting.SourceEntityId,
                            new(cancellation.IdempotencyKey, cancellation.Reason),
                            userId,
                            cancellationToken);
                    break;
                case ErpPostingSourceType.Shipment:
                    await scope.ServiceProvider.GetRequiredService<IShippingOperationService>()
                        .CancelAfterErpDeletionAsync(
                            posting.SourceEntityId,
                            new(cancellation.IdempotencyKey, cancellation.Reason),
                            userId,
                            cancellationToken);
                    break;
                default:
                    throw AppException.BadRequest("Desteklenmeyen ERP kaynak tipi.");
            }

            cancellation.Status = ErpCancellationStatus.Succeeded;
            cancellation.WmsReversedAtUtc = DateTimeOffset.UtcNow;
            cancellation.CompletedAtUtc = DateTimeOffset.UtcNow;
            cancellation.LastErrorCode = null;
            cancellation.LastErrorMessage = null;
            Cancellations.Update(cancellation);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await audit.WriteAsync(new(
                "erp-document.cancel",
                nameof(ErpPostingRecord),
                posting.Id.ToString(),
                "Succeeded",
                "erp-integration",
                cancellation.Reason,
                NewValues: new
                {
                    posting.SourceType,
                    posting.SourceEntityId,
                    posting.SourceDocumentNo,
                    ErpRecordId = erpRecordId,
                    ErpDeleteId = erpDeleteId,
                    cancellation.IdempotencyKey
                },
                ChangedFields: ["ERP document", "Stock movements", "Balances", "Reservations"]), CancellationToken.None);
        }
        catch (Exception ex)
        {
            cancellation.Status = ErpCancellationStatus.CompensationRequired;
            cancellation.CompletedAtUtc = DateTimeOffset.UtcNow;
            cancellation.LastErrorCode = "WMS_REVERSAL_FAILED_AFTER_ERP_DELETE";
            cancellation.LastErrorMessage = Truncate(ex.Message, 4000);
            Cancellations.Update(cancellation);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex,
                "ERP belgesi silindi ancak WMS ters hareketi tamamlanamadı SourceType={SourceType} SourceId={SourceId}",
                posting.SourceType,
                posting.SourceEntityId);
            await audit.WriteAsync(new(
                "erp-document.cancel",
                nameof(ErpPostingRecord),
                posting.Id.ToString(),
                "CompensationRequired",
                "erp-integration",
                cancellation.Reason,
                FailureReason: cancellation.LastErrorMessage,
                NewValues: new
                {
                    posting.SourceType,
                    posting.SourceEntityId,
                    ErpRecordId = erpRecordId,
                    ErpDeleteId = erpDeleteId
                },
                ChangedFields: ["ERP document"]), CancellationToken.None);
        }

        return ToResult(cancellation, posting, erpRecordId);
    }

    private async Task ValidateSourceAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        CancellationToken cancellationToken)
    {
        switch (sourceType)
        {
            case ErpPostingSourceType.GoodsReceipt:
            {
                var header = await unitOfWork.Repository<GoodsReceiptHeader>().FindByIdAsync(
                    sourceEntityId, cancellationToken: cancellationToken)
                    ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
                if (header.Status == WarehouseOperationStatus.Cancelled)
                    throw AppException.Conflict("Mal kabul zaten iptal edilmiş.");
                if (header.ErpIntegrationStatus != ErpIntegrationStatus.Succeeded)
                    throw AppException.Conflict("Mal kabul ERP durumu silme işlemine uygun değil.");
                break;
            }
            case ErpPostingSourceType.WarehouseTransfer:
            {
                var header = await unitOfWork.Repository<WarehouseTransferHeader>().FindByIdAsync(
                    sourceEntityId, cancellationToken: cancellationToken)
                    ?? throw AppException.NotFound("Transfer kaydı bulunamadı.");
                if (header.Status == WarehouseTransferStatus.Cancelled)
                    throw AppException.Conflict("Transfer zaten iptal edilmiş.");
                if (header.ErpIntegrationStatus != ErpIntegrationStatus.Succeeded)
                    throw AppException.Conflict("Transfer ERP durumu silme işlemine uygun değil.");
                break;
            }
            case ErpPostingSourceType.Shipment:
            {
                var header = await unitOfWork.Repository<ShipmentHeader>().FindByIdAsync(
                    sourceEntityId, cancellationToken: cancellationToken)
                    ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");
                if (header.Status == ShipmentStatus.Cancelled)
                    throw AppException.Conflict("Sevk zaten iptal edilmiş.");
                if (header.ErpIntegrationStatus != ErpIntegrationStatus.Succeeded)
                    throw AppException.Conflict("Sevk ERP durumu silme işlemine uygun değil.");
                break;
            }
            default:
                throw AppException.BadRequest("Desteklenmeyen ERP kaynak tipi.");
        }
    }

    private async Task SetSourceErpStatusAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        ErpIntegrationStatus status,
        CancellationToken cancellationToken)
    {
        var affected = sourceType switch
        {
            ErpPostingSourceType.GoodsReceipt => await unitOfWork.Repository<GoodsReceiptHeader>().Query()
                .Where(x => x.Id == sourceEntityId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(v => v.ErpIntegrationStatus, status)
                    .SetProperty(v => v.UpdatedDate, DateTime.UtcNow), cancellationToken),
            ErpPostingSourceType.WarehouseTransfer => await unitOfWork.Repository<WarehouseTransferHeader>().Query()
                .Where(x => x.Id == sourceEntityId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(v => v.ErpIntegrationStatus, status)
                    .SetProperty(v => v.UpdatedDate, DateTime.UtcNow), cancellationToken),
            ErpPostingSourceType.Shipment => await unitOfWork.Repository<ShipmentHeader>().Query()
                .Where(x => x.Id == sourceEntityId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(v => v.ErpIntegrationStatus, status)
                    .SetProperty(v => v.UpdatedDate, DateTime.UtcNow), cancellationToken),
            _ => throw AppException.BadRequest("Desteklenmeyen ERP kaynak tipi.")
        };
        if (affected != 1)
            throw AppException.Conflict("ERP iptal durumu kaynak belgeye uygulanamadı.");
    }

    private async Task<ErpPostingRecord> LoadPostingAsync(
        ErpPostingSourceType sourceType,
        long sourceEntityId,
        bool tracking,
        CancellationToken cancellationToken) =>
        await Postings.FirstOrDefaultAsync(
            x => x.SourceType == sourceType && x.SourceEntityId == sourceEntityId,
            tracking,
            cancellationToken)
        ?? throw AppException.NotFound("ERP gönderim kaydı bulunamadı.");

    private async Task MarkClientCancellationUncertainAsync(
        ErpCancellationRecord cancellation,
        string erpDeleteId,
        long userId)
    {
        cancellation.Status = ErpCancellationStatus.CommitUncertain;
        cancellation.CompletedAtUtc = DateTimeOffset.UtcNow;
        cancellation.LastErrorCode = "REQUEST_CANCELLED_COMMIT_UNCERTAIN";
        cancellation.LastErrorMessage = "İstek ERP silme yanıtı alınmadan iptal edildi; belge Netsis üzerinden doğrulanmalıdır.";
        Cancellations.Update(cancellation);
        await Attempts.AddAsync(new ErpCancellationAttempt
        {
            BranchCode = cancellation.BranchCode,
            ErpCancellationRecordId = cancellation.Id,
            AttemptNo = cancellation.AttemptCount,
            Operation = "DeleteItemSlip",
            Endpoint = $"{optionsAccessor.Value.Rest.ItemSlipsPath.TrimEnd('/')}/{erpDeleteId}",
            IsSuccessful = false,
            CommitUncertain = true,
            ErrorCode = cancellation.LastErrorCode,
            ErrorMessage = cancellation.LastErrorMessage,
            TraceId = cancellation.TraceId,
            StartedAtUtc = cancellation.StartedAtUtc ?? DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            CreatedBy = userId
        }, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<NetsisItemSlipDeleteRequest> ResolveDeleteRequestAsync(
        ErpPostingRecord posting,
        CancellationToken cancellationToken)
    {
        var documentNo = Clean(posting.ErpDocumentNo) ?? Clean(posting.SourceDocumentNo)
            ?? throw AppException.Conflict(
                "ERP belge numarası bulunamadı. İptalden önce ERP gönderim mutabakatında FATIRS_NO doğrulanmalıdır.");
        var options = optionsAccessor.Value.Rest;

        return posting.SourceType switch
        {
            ErpPostingSourceType.GoodsReceipt => new(
                options.GoodsReceiptDocumentType,
                documentNo,
                (await unitOfWork.Repository<GoodsReceiptHeader>().FindByIdAsync(
                    posting.SourceEntityId,
                    cancellationToken: cancellationToken)
                    ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı."))
                .SupplierCodeSnapshot),
            ErpPostingSourceType.WarehouseTransfer => new(
                options.WarehouseTransferDocumentType,
                documentNo,
                null),
            ErpPostingSourceType.Shipment => new(
                options.ShipmentDocumentType,
                documentNo,
                (await unitOfWork.Repository<ShipmentHeader>().FindByIdAsync(
                    posting.SourceEntityId,
                    cancellationToken: cancellationToken)
                    ?? throw AppException.NotFound("Sevk kaydı bulunamadı."))
                .CustomerCodeSnapshot),
            _ => throw AppException.BadRequest("Desteklenmeyen ERP kaynak tipi.")
        };
    }

    private static long ResolveErpRecordIdOrZero(ErpPostingRecord posting)
    {
        if (posting.ErpRecordId is > 0) return posting.ErpRecordId.Value;
        return long.TryParse(posting.ErpRecordNo, out var parsed) && parsed > 0 ? parsed : 0;
    }

    private static void ValidateRequest(long sourceEntityId, Guid idempotencyKey, string reason)
    {
        if (sourceEntityId <= 0 || idempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Kaynak kayıt ve idempotency anahtarı zorunludur.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 || reason.Trim().Length > 1000)
            throw AppException.BadRequest("İptal nedeni 5-1000 karakter arasında olmalıdır.");
    }

    private static void EnsureSameRequest(
        ErpCancellationRecord cancellation,
        Guid idempotencyKey,
        string requestHash)
    {
        if (cancellation.IdempotencyKey != idempotencyKey
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(cancellation.RequestHash),
                Convert.FromHexString(requestHash)))
            throw AppException.Conflict("Bu belge için farklı içerikte bir ERP iptal süreci zaten başlatılmış.");
    }

    private static string Hash(object value) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, HashOptions))));

    private string TraceId() =>
        Activity.Current?.TraceId.ToString()
        ?? httpContextAccessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString("N");

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ErpCancellationResult ToResult(
        ErpCancellationRecord cancellation,
        ErpPostingRecord posting,
        long erpRecordId) => new(
        cancellation.Id,
        posting.Id,
        posting.SourceType,
        posting.SourceEntityId,
        posting.SourceDocumentNo,
        erpRecordId,
        cancellation.Status,
        cancellation.AttemptCount,
        cancellation.LastErrorCode,
        cancellation.LastErrorMessage,
        cancellation.ErpDeletedAtUtc,
        cancellation.WmsReversedAtUtc,
        cancellation.CompletedAtUtc);
}
