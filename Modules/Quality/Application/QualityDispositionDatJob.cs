using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.Quality.Application;

public sealed class QualityDispositionDatJob(
    IUnitOfWork unitOfWork,
    IWarehouseTransferOperationService transferOperations,
    IErpPostingService erpPosting,
    IGoodsReceiptErpPostingCoordinator goodsReceiptErpPosting,
    ILogger<QualityDispositionDatJob> logger) : IGoodsReceiptErpSuccessJob
{
    private const int RecoveryBatchSize = 100;

    public async Task ProcessGoodsReceiptAsync(
        long goodsReceiptId,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await unitOfWork.Repository<GoodsReceiptHeader>().Query()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == goodsReceiptId, cancellationToken);
        if (receipt is null)
        {
            logger.LogWarning(
                "Quality DAT follow-up skipped because goods receipt was not found. GoodsReceiptId={GoodsReceiptId}",
                goodsReceiptId);
            return;
        }

        if (receipt.ErpIntegrationStatus != ErpIntegrationStatus.Succeeded)
        {
            logger.LogInformation(
                "Quality DAT follow-up is waiting for the goods-receipt ERP posting. GoodsReceiptId={GoodsReceiptId} ErpStatus={ErpStatus}",
                goodsReceiptId,
                receipt.ErpIntegrationStatus);
            return;
        }

        var actor = ResolveActor(receipt, actorUserId);
        var transferIds = await LinkedTransferIds(goodsReceiptId)
            .ToListAsync(cancellationToken);
        if (transferIds.Count == 0)
            return;

        var retryableFailures = new List<Exception>();
        foreach (var transferId in transferIds)
        {
            try
            {
                await CompleteAndPostAsync(transferId, actor, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Quality DAT follow-up failed and will be retried. GoodsReceiptId={GoodsReceiptId} TransferId={TransferId}",
                    goodsReceiptId,
                    transferId);
                retryableFailures.Add(exception);
            }
        }

        if (retryableFailures.Count > 0)
            throw new AggregateException(
                $"{retryableFailures.Count} quality DAT follow-up operation(s) could not be completed.",
                retryableFailures);
    }

    public async Task RetryPendingAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await (
                from disposition in unitOfWork.Repository<QualityInspectionDisposition>().Query()
                join inspection in unitOfWork.Repository<QualityInspection>().Query()
                    on disposition.QualityInspectionId equals inspection.Id
                join receipt in unitOfWork.Repository<GoodsReceiptHeader>().Query()
                    on inspection.SourceDocumentId equals receipt.Id
                join transfer in unitOfWork.Repository<WarehouseTransferHeader>().Query()
                    on disposition.WarehouseTransferId equals transfer.Id
                where inspection.SourceDocumentType == "GoodsReceipt"
                    && inspection.DecidedAtUtc.HasValue
                    && (inspection.Status == QualityInspectionStatus.Passed
                        || inspection.Status == QualityInspectionStatus.Failed
                        || inspection.Status == QualityInspectionStatus.Quarantined
                        || inspection.Status == QualityInspectionStatus.Released)
                    && disposition.WarehouseTransferId.HasValue
                    && (receipt.ErpIntegrationStatus == ErpIntegrationStatus.Pending
                        || receipt.ErpIntegrationStatus == ErpIntegrationStatus.Succeeded)
                    && transfer.Status != WarehouseTransferStatus.Cancelled
                    && (transfer.Status != WarehouseTransferStatus.Completed
                        || transfer.ErpIntegrationStatus != ErpIntegrationStatus.Succeeded)
                select new
                {
                    receipt.Id,
                    receipt.ErpIntegrationStatus
                })
            .Distinct()
            .OrderBy(x => x.Id)
            .Take(RecoveryBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            try
            {
                if (candidate.ErpIntegrationStatus == ErpIntegrationStatus.Pending)
                {
                    // This covers decisions committed before the quarantine-aware quality
                    // gate was introduced, and transient enqueue failures after deployment.
                    // A successful goods-receipt post enqueues the DAT follow-up itself.
                    await goodsReceiptErpPosting.PostIfEligibleAsync(
                        candidate.Id,
                        0,
                        cancellationToken);
                    continue;
                }

                await ProcessGoodsReceiptAsync(candidate.Id, 0, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The recurring recovery scan deliberately continues with other receipts.
                // This receipt remains queryable and will be attempted again in the next run.
                logger.LogError(
                    exception,
                    "Pending quality DAT recovery failed. GoodsReceiptId={GoodsReceiptId}",
                    candidate.Id);
            }
        }
    }

    private async Task CompleteAndPostAsync(
        long transferId,
        long actor,
        CancellationToken cancellationToken)
    {
        var transfer = await unitOfWork.Repository<WarehouseTransferHeader>().Query()
            .AsNoTracking()
            .SingleAsync(x => x.Id == transferId, cancellationToken);
        if (transfer.BusinessContext != WarehouseTransferBusinessContext.QualityDisposition
            || transfer.Status == WarehouseTransferStatus.Cancelled)
            return;

        if (transfer.Status != WarehouseTransferStatus.Completed)
        {
            await transferOperations.CompleteQualityDispositionAsync(
                transferId,
                CreateIdempotencyKey(transferId, "stock-completion"),
                actor,
                cancellationToken);
            transfer = await unitOfWork.Repository<WarehouseTransferHeader>().Query()
                .AsNoTracking()
                .SingleAsync(x => x.Id == transferId, cancellationToken);
        }

        if (transfer.ErpIntegrationStatus is ErpIntegrationStatus.Succeeded
            or ErpIntegrationStatus.Cancelled)
            return;
        if (transfer.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain)
        {
            logger.LogCritical(
                "Quality DAT ERP result is uncertain; automatic reposting is blocked to prevent a duplicate ERP document. TransferId={TransferId} DocumentNo={DocumentNo}",
                transfer.Id,
                transfer.DocumentNo);
            return;
        }

        var result = await erpPosting.PostWarehouseTransferAsync(
            transferId,
            CreateIdempotencyKey(transferId, "erp-posting"),
            actor,
            cancellationToken);
        if (result.Status == ErpPostingStatus.CommitUncertain)
        {
            logger.LogCritical(
                "Quality DAT ERP posting became uncertain; manual reconciliation is required. TransferId={TransferId} DocumentNo={DocumentNo}",
                transfer.Id,
                transfer.DocumentNo);
            return;
        }
        if (result.Status != ErpPostingStatus.Succeeded)
            throw new InvalidOperationException(
                $"Quality DAT ERP posting failed. TransferId={transferId}, Status={result.Status}, Code={result.ErrorCode}.");
    }

    private IQueryable<long> LinkedTransferIds(long goodsReceiptId) =>
        (from disposition in unitOfWork.Repository<QualityInspectionDisposition>().Query()
         join inspection in unitOfWork.Repository<QualityInspection>().Query()
             on disposition.QualityInspectionId equals inspection.Id
         where inspection.SourceDocumentType == "GoodsReceipt"
             && inspection.SourceDocumentId == goodsReceiptId
             && disposition.WarehouseTransferId.HasValue
         select disposition.WarehouseTransferId!.Value)
        .Distinct()
        .OrderBy(x => x);

    internal static Guid CreateIdempotencyKey(long transferId, string phase)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"quality-dat:{transferId}:{phase}:v1"));
        return new Guid(hash.AsSpan(0, 16));
    }

    internal static long ResolveActor(GoodsReceiptHeader receipt, long requestedActor) =>
        requestedActor > 0
            ? requestedActor
            : receipt.ReceivedBy ?? receipt.UpdatedBy ?? receipt.CreatedBy ?? 0;
}
