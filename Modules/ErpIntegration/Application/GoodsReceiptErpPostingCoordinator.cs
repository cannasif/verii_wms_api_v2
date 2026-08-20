using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed class GoodsReceiptErpPostingCoordinator(
    IUnitOfWork unitOfWork,
    IErpPostingService erpPostingService,
    IBackgroundJobClient backgroundJobs,
    ILogger<GoodsReceiptErpPostingCoordinator> logger) : IGoodsReceiptErpPostingCoordinator
{
    public async Task<ErpPostingResult?> PostIfEligibleAsync(
        long goodsReceiptId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        if (goodsReceiptId <= 0)
            throw AppException.BadRequest("Mal kabul kaydı seçilmelidir.");

        var header = await unitOfWork.Repository<GoodsReceiptHeader>().Query()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == goodsReceiptId, cancellationToken);

        if (header is null)
            throw AppException.NotFound("Mal kabul kaydı bulunamadı.");

        if (header.ErpIntegrationStatus == ErpIntegrationStatus.Succeeded)
        {
            EnqueueFollowUp(header.Id, actorUserId);
            return null;
        }

        if (header.ErpIntegrationStatus == ErpIntegrationStatus.Cancelled)
        {
            return null;
        }

        if (header.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain)
            throw CommitUncertain(header.DocumentNo);

        var qualitySources = await unitOfWork.Repository<GoodsReceiptLine>().Query()
            .Where(x => x.GrHeaderId == header.Id && x.RequireQualityControl)
            .Select(x => x.QualityRoutingSource)
            .ToListAsync(cancellationToken);
        var hasManualQualityPlan = qualitySources.Contains(GoodsReceiptQualityRoutingSource.ManualReceipt);
        var hasRuleBasedQualityPlan = qualitySources.Any(x => x is
            GoodsReceiptQualityRoutingSource.StockRule
            or GoodsReceiptQualityRoutingSource.StockGroupRule
            or GoodsReceiptQualityRoutingSource.GlobalDefault);
        if (header.RequireQualityControl && qualitySources.Count > 0
            && !hasManualQualityPlan && !hasRuleBasedQualityPlan)
            hasRuleBasedQualityPlan = true;
        var hasConclusiveQualityInspection =
            await GoodsReceiptQualityGate.HasConclusiveInspectionAsync(
                unitOfWork,
                header.Id,
                cancellationToken);

        if (!GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
                header.Status,
                header.ApprovalStatus,
                header.QualityStatus,
                header.ErpPostingPolicy,
                header.ErpQualityGatePolicy,
                hasRuleBasedQualityPlan,
                hasManualQualityPlan,
                hasConclusiveQualityInspection))
        {
            return null;
        }

        var result = await erpPostingService.PostGoodsReceiptAsync(
            goodsReceiptId,
            CreateAutomaticIdempotencyKey(goodsReceiptId),
            actorUserId > 0
                ? actorUserId
                : header.ReceivedBy ?? header.UpdatedBy ?? header.CreatedBy ?? 0,
            cancellationToken);

        return result.Status switch
        {
            ErpPostingStatus.Succeeded => result,
            ErpPostingStatus.CommitUncertain => throw CommitUncertain(header.DocumentNo),
            _ => throw PostingFailed(header.DocumentNo, result)
        };
    }

    private static AppException PostingFailed(string documentNo, ErpPostingResult result)
    {
        var detail = string.Join(
            " - ",
            new[] { result.ErrorCode, result.ErrorMessage }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        if (string.IsNullOrWhiteSpace(detail))
            detail = "Netsis servisinden başarısız yanıt alındı.";

        return AppException.BadGateway(
            $"{documentNo} numaralı mal kabul adımı WMS'te tamamlandı ancak Netsis irsaliyesi oluşturulamadı: {detail} " +
            "Mal Kabul Listesi'ndeki ERP'ye Gönder işlemiyle tekrar deneyebilirsiniz.");
    }

    private static AppException CommitUncertain(string documentNo)
    {
        return AppException.Conflict(
            $"{documentNo} numaralı mal kabul adımı WMS'te tamamlandı ancak Netsis yanıtı kesinleşmedi. " +
            "Mükerrer irsaliye riskini önlemek için Netsis kontrol edilmeden tekrar göndermeyin.");
    }

    private static Guid CreateAutomaticIdempotencyKey(long goodsReceiptId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"goods-receipt:{goodsReceiptId}:automatic-erp-posting:v1"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private void EnqueueFollowUp(long goodsReceiptId, long actorUserId)
    {
        try
        {
            backgroundJobs.Enqueue<IGoodsReceiptErpSuccessJob>(job =>
                job.ProcessGoodsReceiptAsync(goodsReceiptId, actorUserId, CancellationToken.None));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Existing goods-receipt ERP success could not queue quality DAT follow-up. GoodsReceiptId={GoodsReceiptId}",
                goodsReceiptId);
        }
    }
}

internal static class GoodsReceiptQualityGate
{
    internal static async Task<bool> HasConclusiveInspectionAsync(
        IUnitOfWork unitOfWork,
        long goodsReceiptId,
        CancellationToken cancellationToken)
    {
        var inspectionStates = await unitOfWork.Repository<QualityInspection>().Query()
            .Where(inspection => inspection.SourceDocumentType == "GoodsReceipt"
                && inspection.SourceDocumentId == goodsReceiptId
                && inspection.Status != QualityInspectionStatus.Cancelled)
            .Select(inspection => new
            {
                inspection.Status,
                inspection.DecidedAtUtc
            })
            .ToListAsync(cancellationToken);

        return inspectionStates.Count > 0
            && inspectionStates.All(inspection => IsConclusiveInspection(
                inspection.Status,
                inspection.DecidedAtUtc));
    }

    internal static bool IsConclusiveInspection(
        QualityInspectionStatus status,
        DateTimeOffset? decidedAtUtc) =>
        decidedAtUtc.HasValue
        && status is QualityInspectionStatus.Passed
            or QualityInspectionStatus.Failed
            or QualityInspectionStatus.Quarantined
            or QualityInspectionStatus.Released;
}
