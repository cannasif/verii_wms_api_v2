using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed class GoodsReceiptErpAutomation(
    IBackgroundJobClient backgroundJobs,
    ILogger<GoodsReceiptErpAutomation> logger) : IGoodsReceiptErpAutomation
{
    public void Enqueue(long goodsReceiptId, long actorUserId)
    {
        if (goodsReceiptId <= 0) return;

        try
        {
            backgroundJobs.Enqueue<IGoodsReceiptErpPostingJob>(job =>
                job.PostIfEligibleAsync(goodsReceiptId, actorUserId, CancellationToken.None));
        }
        catch (Exception exception)
        {
            // The receipt transaction has already committed. Do not turn a successful warehouse
            // operation into an HTTP error when Hangfire is temporarily unavailable; the minutely
            // recovery sweep will pick up the still-pending ERP document.
            logger.LogError(
                exception,
                "Could not enqueue automatic ERP posting for goods receipt {GoodsReceiptId}.",
                goodsReceiptId);
        }
    }
}

public sealed class GoodsReceiptErpPostingJob(
    IUnitOfWork unitOfWork,
    IErpPostingService erpPosting,
    IBackgroundJobClient backgroundJobs,
    ILogger<GoodsReceiptErpPostingJob> logger) : IGoodsReceiptErpPostingJob
{
    public async Task PostIfEligibleAsync(
        long goodsReceiptId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var header = await unitOfWork.Repository<GoodsReceiptHeader>().Query()
            .SingleOrDefaultAsync(x => x.Id == goodsReceiptId, cancellationToken);
        if (header is null)
        {
            logger.LogWarning(
                "Automatic ERP posting skipped because goods receipt {GoodsReceiptId} was not found.",
                goodsReceiptId);
            return;
        }

        if (header.ErpIntegrationStatus is not (ErpIntegrationStatus.Pending or ErpIntegrationStatus.Failed)
            || !GoodsReceiptErpPostingPolicyEvaluator.IsEligible(
                header.Status,
                header.ApprovalStatus,
                header.QualityStatus,
                header.ErpPostingPolicy))
        {
            return;
        }

        try
        {
            var result = await erpPosting.PostGoodsReceiptAsync(
                header.Id,
                CreateIdempotencyKey(header.Id),
                actorUserId > 0
                    ? actorUserId
                    : header.ReceivedBy ?? header.UpdatedBy ?? header.CreatedBy ?? 0,
                cancellationToken);

            if (result.Status == ErpPostingStatus.Failed)
                throw new InvalidOperationException(
                    $"Automatic ERP posting failed: {result.ErrorCode ?? "UNKNOWN"} - {result.ErrorMessage}");

            if (result.Status == ErpPostingStatus.CommitUncertain)
                logger.LogError(
                    "Automatic ERP posting is commit-uncertain for goods receipt {GoodsReceiptId}; manual reconciliation is required.",
                    header.Id);
        }
        catch (AppException exception)
        {
            // Business/gate failures are not blindly retried. A later state transition or the
            // recovery sweep queues the document again when it becomes safely eligible.
            logger.LogWarning(
                exception,
                "Automatic ERP posting was skipped for goods receipt {GoodsReceiptId}: {Message}",
                header.Id,
                exception.Message);
        }
    }

    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var candidates = await unitOfWork.Repository<GoodsReceiptHeader>().Query()
            .Where(x => x.ErpIntegrationStatus == ErpIntegrationStatus.Pending
                && (x.Status == WarehouseOperationStatus.Processed
                    || x.Status == WarehouseOperationStatus.Completed)
                && (x.ErpPostingPolicy == GoodsReceiptErpPostingPolicy.AfterReceipt
                    || x.ErpPostingPolicy == GoodsReceiptErpPostingPolicy.AfterReceiptApproval
                        && (x.ApprovalStatus == OperationApprovalStatus.NotRequired
                            || x.ApprovalStatus == OperationApprovalStatus.Approved)
                    || x.ErpPostingPolicy == GoodsReceiptErpPostingPolicy.AfterQualityApproval
                        && (x.QualityStatus == OperationQualityStatus.NotRequired
                            || x.QualityStatus == OperationQualityStatus.Passed)
                    || x.ErpPostingPolicy == GoodsReceiptErpPostingPolicy.AfterAllApprovals
                        && (x.ApprovalStatus == OperationApprovalStatus.NotRequired
                            || x.ApprovalStatus == OperationApprovalStatus.Approved)
                        && (x.QualityStatus == OperationQualityStatus.NotRequired
                            || x.QualityStatus == OperationQualityStatus.Passed)))
            .OrderBy(x => x.Id)
            .Take(200)
            .Select(x => new
            {
                x.Id,
                ActorUserId = x.ReceivedBy ?? x.UpdatedBy ?? x.CreatedBy ?? 0
            })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            backgroundJobs.Enqueue<IGoodsReceiptErpPostingJob>(job =>
                job.PostIfEligibleAsync(candidate.Id, candidate.ActorUserId, CancellationToken.None));
        }

        if (candidates.Count > 0)
            logger.LogInformation(
                "Queued {Count} eligible pending goods receipts for automatic ERP posting.",
                candidates.Count);
    }

    private static Guid CreateIdempotencyKey(long goodsReceiptId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"goods-receipt:{goodsReceiptId}:automatic-erp-posting:v1"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
