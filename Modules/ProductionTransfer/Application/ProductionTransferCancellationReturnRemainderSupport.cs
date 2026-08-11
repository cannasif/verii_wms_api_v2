using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferCancellationReturnRemainderSupport
{
    public static async Task ReleaseUnpickedRemainderToWorkOrderAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        IReadOnlyList<WarehouseTransferLine> remainingLines,
        long actor,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (remainingLines.Count == 0)
            return;

        var link = await uow.Repository<ProductionTransferHeaderLink>().Query(true)
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .SingleOrDefaultAsync(x => x.WarehouseTransferHeaderId == header.Id, ct);
        if (link is null)
            return;

        foreach (var line in remainingLines)
        {
            if (line.IsDeleted)
                continue;

            line.IsDeleted = true;
            line.DeletedBy = actor;
            line.DeletedDate = utcNow;
            line.Status = WarehouseTransferLineStatus.Cancelled;
            line.UpdatedBy = actor;
            line.UpdatedDate = utcNow;

            foreach (var tracking in line.Trackings.Where(tracking => !tracking.IsDeleted))
            {
                tracking.IsDeleted = true;
                tracking.DeletedBy = actor;
                tracking.DeletedDate = utcNow;
                tracking.Status = WarehouseTransferTrackingStatus.Cancelled;
                tracking.UpdatedBy = actor;
                tracking.UpdatedDate = utcNow;
            }

            foreach (var task in header.Tasks.Where(task => !task.IsDeleted))
            {
                foreach (var taskLine in task.Lines.Where(taskLine => !taskLine.IsDeleted && taskLine.WtLineId == line.Id))
                {
                    taskLine.IsDeleted = true;
                    taskLine.DeletedBy = actor;
                    taskLine.DeletedDate = utcNow;
                    taskLine.UpdatedBy = actor;
                    taskLine.UpdatedDate = utcNow;
                }
            }

            var productionLineLink = link.Lines.FirstOrDefault(x => !x.IsDeleted && x.WarehouseTransferLineId == line.Id);
            if (productionLineLink is not null)
            {
                productionLineLink.IsDeleted = true;
                productionLineLink.DeletedBy = actor;
                productionLineLink.DeletedDate = utcNow;
                productionLineLink.UpdatedBy = actor;
                productionLineLink.UpdatedDate = utcNow;
            }
        }

        link.UpdatedBy = actor;
        link.UpdatedDate = utcNow;
        header.UpdatedBy = actor;
        header.UpdatedDate = utcNow;
    }

    public static async Task FinalizeTransferAfterProductionCancellationReturnAsync(
        IUnitOfWork uow,
        IWarehouseTransferReservationService reservations,
        WarehouseTransferHeader header,
        WarehouseTransferTask cancellationReturnTask,
        Guid idempotencyKey,
        long actor,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (header.Status == WarehouseTransferStatus.Cancelled)
            return;

        var link = await uow.Repository<ProductionTransferHeaderLink>().Query(true)
            .SingleOrDefaultAsync(x => x.WarehouseTransferHeaderId == header.Id, ct);

        if (WarehouseTransferReservationService.UsesTransferReservations(header))
        {
            await reservations.ReleaseAllAsync(
                header,
                $"WT:{header.Id}:RESERVE:CANCEL-RETURN-FINALIZE:{idempotencyKey:N}",
                "Üretim transfer iptal iadesi sonrası kalan atama iş emrine geri bırakıldı.",
                actor,
                ct);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var pickTask in header.Tasks
                     .Where(x => !x.IsDeleted
                         && x.TaskType == WarehouseTransferTaskType.Pick
                         && x.Status is not WarehouseTransferTaskStatus.Completed
                             and not WarehouseTransferTaskStatus.Cancelled))
        {
            pickTask.Status = WarehouseTransferTaskStatus.Cancelled;
            pickTask.UpdatedBy = actor;
            pickTask.UpdatedDate = utcNow;
            foreach (var assignment in pickTask.Assignments.Where(x => !x.IsDeleted))
            {
                assignment.IsDeleted = true;
                assignment.DeletedBy = actor;
                assignment.DeletedDate = utcNow;
            }
        }

        foreach (var line in header.Lines.Where(x => !x.IsDeleted))
        {
            line.Status = WarehouseTransferLineStatus.Cancelled;
            line.UpdatedBy = actor;
            line.UpdatedDate = utcNow;
        }

        header.Status = WarehouseTransferStatus.Cancelled;
        header.CancelledAtUtc = now;
        header.CancelledBy = actor;
        header.CancellationReason = Clean(
            cancellationReturnTask.Description,
            1000) ?? "Üretim transfer iptal iadesi tamamlandı.";
        header.UpdatedBy = actor;
        header.UpdatedDate = utcNow;
        header.StatusHistory.Add(new WarehouseTransferStatusHistory
        {
            BranchCode = header.BranchCode,
            CreatedBy = actor,
            CreatedDate = utcNow,
            StatusArea = WarehouseTransferStatusArea.Operation,
            ToStatus = WarehouseTransferStatus.Cancelled.ToString(),
            ChangedAtUtc = now,
            ChangedBy = actor,
            Description = "İptal iadesi sonrası kalan malzeme iş emrine geri bırakıldı.",
            CorrelationId = idempotencyKey,
        });

        if (link is not null)
        {
            link.WorkflowStatus = ProductionTransferWorkflowStatus.Cancelled;
            link.UpdatedBy = actor;
            link.UpdatedDate = utcNow;
        }
    }

    private static string? Clean(string? value, int maxLength)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return cleaned is { Length: > 0 } && cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }
}
