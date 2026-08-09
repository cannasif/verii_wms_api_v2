using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public interface IKkdDistributionCompletionService
{
    Task<KkdDistributionCompleteResult> CompleteByDistributionAsync(long distributionId, Guid idempotencyKey, long actor, CancellationToken ct = default);
    Task<KkdDistributionCompleteResult?> CompleteByWarehouseOutboundAsync(long warehouseOutboundId, Guid idempotencyKey, long actor, CancellationToken ct = default);
}

public sealed class KkdDistributionCompletionService(IUnitOfWork uow, IErpPostingService erp, IAuditLogWriter audit)
    : IKkdDistributionCompletionService
{
    public async Task<KkdDistributionCompleteResult?> CompleteByWarehouseOutboundAsync(
        long warehouseOutboundId, Guid idempotencyKey, long actor, CancellationToken ct = default)
    {
        var distributionId = await uow.Repository<KkdDistribution>().Query()
            .Where(x => x.WarehouseOutboundId == warehouseOutboundId)
            .Select(x => (long?)x.Id)
            .SingleOrDefaultAsync(ct);
        return distributionId.HasValue
            ? await CompleteByDistributionAsync(distributionId.Value, idempotencyKey, actor, ct)
            : null;
    }

    public async Task<KkdDistributionCompleteResult> CompleteByDistributionAsync(
        long distributionId, Guid idempotencyKey, long actor, CancellationToken ct = default)
    {
        if (distributionId <= 0 || idempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Dağıtım ve idempotency anahtarı zorunludur.");
        var completed = await uow.ExecuteInTransactionAsync(async token =>
        {
            var entity = await uow.Repository<KkdDistribution>().Query(true)
                .Include(x => x.Lines).ThenInclude(x => x.EntitlementAllocations)
                .SingleOrDefaultAsync(x => x.Id == distributionId, token)
                ?? throw AppException.NotFound("KKD dağıtımı bulunamadı.");
            if (entity.Status == KkdDistributionStatus.Cancelled)
                throw AppException.Conflict("İptal edilmiş KKD dağıtımı tamamlanamaz.");
            if (entity.ExcessApprovalStatus == KkdExcessApprovalStatus.Pending)
                throw AppException.Conflict("Kota aşımı için depo yöneticisinin fiziksel kontrol onayı bekleniyor.");
            // Reddedilen aşım kalemleri karar anında belgeden düşürülür (bkz. DecideExcessApprovalAsync).
            // Geriye kalan kalemlerde hâlâ aşım varsa (örn. hiç hak edilen miktar yoksa) belge tamamlanamaz.
            if (entity.ExcessApprovalStatus == KkdExcessApprovalStatus.Rejected
                && entity.Lines.Any(l => l.ExcessQuantity > 0))
                throw AppException.Conflict("Kota aşımı reddedilmiş KKD dağıtımı tamamlanamaz.");
            if (!entity.WarehouseOutboundId.HasValue)
                throw AppException.Conflict("KKD dağıtımının ambar çıkış belgesi bulunmuyor.");
            var outboundStatus = await uow.Repository<WarehouseOutboundHeader>().Query()
                .Where(x => x.Id == entity.WarehouseOutboundId.Value)
                .Select(x => (WarehouseOutboundStatus?)x.Status)
                .SingleOrDefaultAsync(token)
                ?? throw AppException.Conflict("Bağlı ambar çıkış belgesi bulunamadı.");
            if (outboundStatus != WarehouseOutboundStatus.Shipped)
                throw AppException.Conflict("KKD tesliminden önce bağlı ambar çıkışı kesinleştirilmelidir.");
            var replayed = entity.Status == KkdDistributionStatus.Completed;
            if (!replayed)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var line in entity.Lines)
                foreach (var allocation in line.EntitlementAllocations)
                    await uow.Repository<KkdEntitlementConsumption>().AddAsync(
                        await BuildConsumptionAsync(entity, line, allocation, now, actor, token), token);
                entity.Status = KkdDistributionStatus.Completed;
                entity.CompletedAtUtc = now;
                entity.UpdatedBy = actor;
                entity.UpdatedDate = DateTime.UtcNow;
                if (entity.KkdRequestId.HasValue)
                {
                    var request = await uow.Repository<KkdRequest>().Query(true).Include(x => x.Lines)
                        .SingleOrDefaultAsync(x => x.Id == entity.KkdRequestId.Value, token)
                        ?? throw AppException.Conflict("Bağlı KKD talebi bulunamadı.");
                    var lines = request.Lines.ToDictionary(x => x.Id);
                    foreach (var distributionLine in entity.Lines.Where(x => x.KkdRequestLineId.HasValue))
                    {
                        if (!lines.TryGetValue(distributionLine.KkdRequestLineId!.Value, out var requestLine))
                            throw AppException.Conflict("Bağlı KKD talep kalemi bulunamadı.");
                        if (requestLine.AllocatedQuantity < distributionLine.Quantity)
                            throw AppException.Conflict("KKD talep kalemindeki ayrılmış miktar dağıtım miktarıyla uyuşmuyor.");
                        requestLine.AllocatedQuantity -= distributionLine.Quantity;
                        requestLine.DeliveredQuantity += distributionLine.Quantity;
                        requestLine.UpdatedBy = actor;
                        requestLine.UpdatedDate = now.UtcDateTime;
                    }
                    request.UpdatedBy = actor;
                    request.UpdatedDate = now.UtcDateTime;
                    KkdRequestStateMachine.Refresh(request, now);
                    await KkdPreparationTaskProgress.ApplyDeliveryAsync(
                        uow,
                        entity.Lines.Where(x => x.KkdRequestLineId.HasValue)
                            .GroupBy(x => x.KkdRequestLineId!.Value)
                            .ToDictionary(x => x.Key, x => x.Sum(item => item.Quantity)),
                        actor, now, token);
                }
                await uow.SaveChangesAsync(token);
            }
            return (Entity: entity, Replayed: replayed);
        }, ct, IsolationLevel.Serializable);

        var erpResult = await erp.PostWarehouseOutboundAsync(
            completed.Entity.WarehouseOutboundId!.Value, idempotencyKey, actor, ct);
        if (!completed.Replayed)
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.distribution.complete", nameof(KkdDistribution), completed.Entity.Id.ToString(), "Succeeded", "kkd-distribution",
                NewValues: new { completed.Entity.Status, ErpStatus = erpResult.Status.ToString() },
                ChangedFields: ["Status"]), ct);
        return new(completed.Entity.Id, completed.Entity.DocumentNo, completed.Entity.Status.ToString(),
            completed.Entity.WarehouseOutboundId.Value, WarehouseOutboundStatus.Shipped.ToString(),
            erpResult.Status.ToString(), completed.Replayed);
    }

    private async Task<KkdEntitlementConsumption> BuildConsumptionAsync(
        KkdDistribution distribution, KkdDistributionLine line,
        KkdDistributionEntitlementAllocation allocation, DateTimeOffset now, long actor, CancellationToken ct)
    {
        long? matrixId = null, ruleId = null, phaseId = null, overrideId = null;
        if (allocation.SourceType == KkdEntitlementSourceType.Matrix)
        {
            var phase = await uow.Repository<KkdEntitlementPhase>().Query()
                .Where(x => x.Id == allocation.SourceId)
                .Select(x => new { PhaseId = x.Id, x.RuleId, x.Rule.MatrixId })
                .SingleOrDefaultAsync(ct)
                ?? throw AppException.Conflict("Ayrılmış KKD matris dönemi artık bulunamıyor.");
            matrixId = phase.MatrixId;
            ruleId = phase.RuleId;
            phaseId = phase.PhaseId;
        }
        else if (allocation.SourceType == KkdEntitlementSourceType.ManualOverride)
        {
            var item = await uow.Repository<KkdEmployeeEntitlementOverride>().FindByIdAsync(allocation.SourceId, true, ct)
                ?? throw AppException.Conflict("Ayrılmış personel ek hakkı artık bulunamıyor.");
            if (!item.IsActive || item.ConsumedQuantity + allocation.Quantity > item.Quantity)
                throw AppException.Conflict("Personel ek hakkı tamamlanmadan önce değişmiş veya tükenmiş.");
            item.ConsumedQuantity += allocation.Quantity;
            overrideId = item.Id;
            ruleId = item.RuleId;
        }
        return new KkdEntitlementConsumption
        {
            BranchCode = distribution.BranchCode,
            EmployeeId = distribution.EmployeeId,
            DistributionId = distribution.Id,
            DistributionLineId = line.Id,
            StockId = line.StockId,
            GroupCode = line.GroupCode,
            SourceType = allocation.SourceType,
            MatrixId = matrixId,
            RuleId = ruleId,
            PhaseId = phaseId,
            OverrideId = overrideId,
            Quantity = allocation.Quantity,
            ConsumedAtUtc = now,
            CreatedBy = actor,
            CreatedDate = now.UtcDateTime
        };
    }
}
