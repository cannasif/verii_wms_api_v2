using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferCancellationReturnRemainderSupport
{
    public static Task ReleaseUnlinkedShortageRemainderToAtanmayanlarAsync(
        IUnitOfWork uow,
        IWarehouseTransferReservationService reservations,
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        Guid idempotencyKey,
        long actor,
        CancellationToken ct) =>
        ReleaseUnlinkedDraftToAtanmayanlarAsync(
            uow,
            reservations,
            header,
            link,
            "Eksik teslim kalanı Atanmayanlar kuyruğuna bırakıldı.",
            idempotencyKey,
            actor,
            ct,
            "Eksik teslim kalanı Atanmayanlar kuyruğuna bırakıldı.");

    public static async Task ReleaseUnlinkedDraftToAtanmayanlarAsync(
        IUnitOfWork uow,
        IWarehouseTransferReservationService reservations,
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        string reason,
        Guid idempotencyKey,
        long actor,
        CancellationToken ct,
        string? statusHistoryDescription = null)
    {
        var utcNow = DateTime.UtcNow;
        var now = DateTimeOffset.UtcNow;

        if (WarehouseTransferReservationService.UsesTransferReservations(header))
        {
            await reservations.ReleaseAllAsync(
                header,
                $"WT:{header.Id}:RESERVE:UNLINKED-DRAFT-CANCEL:{idempotencyKey:N}",
                reason,
                actor,
                ct);
        }

        foreach (var task in header.Tasks.Where(x => !x.IsDeleted).ToArray())
        {
            foreach (var assignment in task.Assignments.Where(x => !x.IsDeleted).ToArray())
            {
                assignment.IsDeleted = true;
                assignment.DeletedBy = actor;
                assignment.DeletedDate = utcNow;
            }

            if (task.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
                continue;

            if (task.TaskType != WarehouseTransferTaskType.Pick)
            {
                task.Status = WarehouseTransferTaskStatus.Cancelled;
                task.UpdatedBy = actor;
                task.UpdatedDate = utcNow;
                continue;
            }

            task.Status = WarehouseTransferTaskStatus.Open;
            task.Description = $"{header.DocumentNo} {ProductionWorkOrderTransferGrouping.UnlinkedPendingReassignmentDescriptionMarker} toplama işi.";
            task.UpdatedBy = actor;
            task.UpdatedDate = utcNow;
        }

        var openPickTask = header.Tasks.FirstOrDefault(x => !x.IsDeleted
            && x.TaskType == WarehouseTransferTaskType.Pick
            && x.Status is not WarehouseTransferTaskStatus.Completed
                and not WarehouseTransferTaskStatus.Cancelled);
        if (openPickTask is null)
        {
            openPickTask = new WarehouseTransferTask
            {
                BranchCode = header.BranchCode,
                Header = header,
                TaskNo = $"{header.DocumentNo}-1",
                TaskType = WarehouseTransferTaskType.Pick,
                WarehouseId = header.SourceWarehouseId,
                Status = WarehouseTransferTaskStatus.Open,
                Priority = header.Priority,
                Description = $"{header.DocumentNo} {ProductionWorkOrderTransferGrouping.UnlinkedPendingReassignmentDescriptionMarker} toplama işi.",
                CreatedBy = actor,
                CreatedDate = utcNow,
            };
            foreach (var line in header.Lines.Where(x => !x.IsDeleted))
            {
                openPickTask.Lines.Add(new WarehouseTransferTaskLine
                {
                    BranchCode = header.BranchCode,
                    CreatedBy = actor,
                    CreatedDate = utcNow,
                    Task = openPickTask,
                    Line = line,
                    WtLineId = line.Id,
                    PlannedQuantity = line.RequestedQuantity,
                    ProcessedQuantity = 0,
                    SourceLocationId = line.DefaultSourceLocationId,
                });
            }

            await uow.Repository<WarehouseTransferTask>().AddAsync(openPickTask, ct);
        }

        link.WorkflowStatus = ProductionTransferWorkflowStatus.Planned;
        link.UpdatedBy = actor;
        link.UpdatedDate = utcNow;
        header.UpdatedBy = actor;
        header.UpdatedDate = utcNow;
        header.StatusHistory.Add(new WarehouseTransferStatusHistory
        {
            BranchCode = header.BranchCode,
            CreatedBy = actor,
            CreatedDate = utcNow,
            StatusArea = WarehouseTransferStatusArea.Operation,
            ToStatus = header.Status.ToString(),
            ChangedAtUtc = now,
            ChangedBy = actor,
            Description = statusHistoryDescription
                ?? "İş emrisiz taslak transfer iptal edildi; belge Atanmayanlar kuyruğuna bırakıldı.",
            CorrelationId = idempotencyKey,
        });
    }

    public static void ReactivateUnlinkedTransferAfterCancellationReturn(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        long actor,
        DateTime utcNow)
    {
        var hasPickedQuantity = header.Lines.Any(line => !line.IsDeleted && line.PickedQuantity > 0);
        header.Status = hasPickedQuantity
            ? WarehouseTransferStatus.Released
            : WarehouseTransferStatus.Draft;
        header.CancelledAtUtc = null;
        header.CancelledBy = null;
        header.UpdatedBy = actor;
        header.UpdatedDate = utcNow;

        foreach (var line in header.Lines.Where(x => !x.IsDeleted))
        {
            if (line.RequestedQuantity <= line.PickedQuantity)
                continue;

            line.Status = line.PickedQuantity > 0
                ? WarehouseTransferLineStatus.PartiallyPicked
                : WarehouseTransferLineStatus.Open;
            line.UpdatedBy = actor;
            line.UpdatedDate = utcNow;
        }

        link.WorkflowStatus = hasPickedQuantity
            ? ProductionTransferWorkflowStatus.Picking
            : ProductionTransferWorkflowStatus.Planned;
        link.UpdatedBy = actor;
        link.UpdatedDate = utcNow;
    }

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
