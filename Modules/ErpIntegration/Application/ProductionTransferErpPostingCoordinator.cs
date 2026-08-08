using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Localization;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpIntegration.Application;

public sealed class ProductionTransferErpPostingCoordinator(
    IUnitOfWork unitOfWork,
    IErpPostingService erpPostingService,
    IAuditLogWriter audit,
    ILogger<ProductionTransferErpPostingCoordinator> logger,
    IStringLocalizer<ProductionTransferResource> localizer) : IProductionTransferErpPostingCoordinator
{
    public async Task<ErpPostingResult?> PostIfEligibleAsync(
        long transferId,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        var aggregate = await LoadAsync(transferId, cancellationToken);
        if (!ProductionTransferErpPostingPolicyEvaluator.IsEligible(
                aggregate.Link.ErpPostingPolicy,
                aggregate.Link.WorkflowStatus,
                aggregate.Header.Status,
                aggregate.Header.ErpIntegrationStatus))
        {
            return null;
        }

        return await TryPostAsync(
            aggregate.Header,
            CreateAutomaticIdempotencyKey(transferId),
            actorUserId,
            cancellationToken);
    }

    public async Task<ErpPostingResult?> PostNowAsync(
        long transferId,
        Guid idempotencyKey,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (idempotencyKey == Guid.Empty)
            throw AppException.BadRequest(localizer["InvalidErpIdempotencyKey"]);

        var aggregate = await LoadAsync(transferId, cancellationToken);
        if (aggregate.Link.ErpPostingPolicy == ProductionTransferErpPostingPolicy.Disabled)
            throw AppException.Conflict(localizer["ErpPostingDisabled"]);
        if (aggregate.Link.WorkflowStatus is not (ProductionTransferWorkflowStatus.Completed
                or ProductionTransferWorkflowStatus.CompletedWithShortage)
            || aggregate.Header.Status is not (WarehouseTransferStatus.Completed
                or WarehouseTransferStatus.CompletedWithShortage))
            throw AppException.Conflict(localizer["TransferMustBeCompleted"]);
        if (aggregate.Header.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain)
            throw AppException.Conflict(localizer["ErpCommitUncertain"]);
        if (aggregate.Header.ErpIntegrationStatus == ErpIntegrationStatus.Processing)
            throw AppException.Conflict(localizer["ErpPostingInProgress"]);
        if (aggregate.Header.ErpIntegrationStatus == ErpIntegrationStatus.Succeeded)
            return await erpPostingService.GetAsync(
                ErpPostingSourceType.WarehouseTransfer,
                transferId,
                cancellationToken);

        return await TryPostAsync(aggregate.Header, idempotencyKey, actorUserId, cancellationToken);
    }

    private async Task<ErpPostingResult> TryPostAsync(
        WarehouseTransferHeader header,
        Guid idempotencyKey,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await erpPostingService.PostWarehouseTransferAsync(
                header.Id,
                idempotencyKey,
                actorUserId > 0 ? actorUserId : header.CompletedBy ?? header.UpdatedBy ?? header.CreatedBy ?? 0,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var userMessage = exception is AppException
                ? exception.Message
                : localizer["ErpUnexpectedFailure"].Value;
            logger.LogError(
                exception,
                "Production transfer ERP posting failed before a conclusive Netsis result. TransferId={TransferId} DocumentNo={DocumentNo}",
                header.Id,
                header.DocumentNo);
            var postingStatus = header.ErpIntegrationStatus switch
            {
                ErpIntegrationStatus.Processing => ErpPostingStatus.Processing,
                ErpIntegrationStatus.CommitUncertain => ErpPostingStatus.CommitUncertain,
                ErpIntegrationStatus.Succeeded => ErpPostingStatus.Succeeded,
                _ => ErpPostingStatus.Failed
            };
            if (postingStatus == ErpPostingStatus.Failed)
                await MarkPreflightFailureAsync(header.Id, actorUserId, userMessage, cancellationToken);
            return new ErpPostingResult(
                0,
                ErpPostingSourceType.WarehouseTransfer,
                header.Id,
                header.DocumentNo,
                postingStatus,
                0,
                null,
                null,
                null,
                null,
                "ERP_PREFLIGHT_FAILURE",
                userMessage,
                DateTimeOffset.UtcNow);
        }
    }

    private async Task MarkPreflightFailureAsync(
        long transferId,
        long actorUserId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var header = await unitOfWork.Repository<WarehouseTransferHeader>().Query(true)
            .SingleAsync(x => x.Id == transferId, cancellationToken);
        if (header.ErpIntegrationStatus is not (ErpIntegrationStatus.Succeeded
                or ErpIntegrationStatus.Processing
                or ErpIntegrationStatus.CommitUncertain
                or ErpIntegrationStatus.Cancelled))
        {
            header.ErpIntegrationStatus = ErpIntegrationStatus.Failed;
            header.UpdatedBy = actorUserId > 0 ? actorUserId : header.UpdatedBy;
            header.UpdatedDate = DateTime.UtcNow;
            unitOfWork.Repository<WarehouseTransferHeader>().Update(header);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await audit.WriteAsync(new(
            "production-transfer.erp.post",
            nameof(WarehouseTransferHeader),
            transferId.ToString(),
            "Failed",
            "production-transfer",
            NewValues: new { header.DocumentNo, Error = errorMessage },
            ChangedFields: ["ErpIntegrationStatus"]), cancellationToken);
    }

    private async Task<(WarehouseTransferHeader Header, ProductionTransferHeaderLink Link)> LoadAsync(
        long transferId,
        CancellationToken cancellationToken)
    {
        if (transferId <= 0)
            throw AppException.BadRequest(localizer["TransferRequired"]);

        var header = await unitOfWork.Repository<WarehouseTransferHeader>().Query()
            .SingleOrDefaultAsync(x => x.Id == transferId, cancellationToken)
            ?? throw AppException.NotFound(localizer["TransferNotFound"]);
        var link = await unitOfWork.Repository<ProductionTransferHeaderLink>().Query()
            .SingleOrDefaultAsync(x => x.WarehouseTransferHeaderId == transferId, cancellationToken)
            ?? throw AppException.NotFound(localizer["TransferLinkNotFound"]);
        return (header, link);
    }

    private static Guid CreateAutomaticIdempotencyKey(long transferId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"production-transfer:{transferId}:automatic-erp-posting:v1"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
