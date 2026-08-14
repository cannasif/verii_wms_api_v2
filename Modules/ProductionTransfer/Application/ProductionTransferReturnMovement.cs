using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferReturnMovement
{
    internal static long? ResolveStagingLocationId(
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        WarehouseTransferTaskLine? pickTaskLine = null)
    {
        var fromTracking = line.Trackings
            .Where(x => x.PickedQuantity > 0 && x.TargetLocationId.HasValue)
            .Select(x => x.TargetLocationId!.Value)
            .Distinct()
            .ToArray();
        if (fromTracking.Length == 1) return fromTracking[0];
        if (pickTaskLine?.TargetLocationId is long pickTarget) return pickTarget;
        return header.SourceStagingLocationId;
    }

    internal static long? ResolveReturnTargetLocationId(
        WarehouseTransferLine line,
        WarehouseTransferTaskLine? pickTaskLine = null)
    {
        var originalSources = line.Trackings
            .Where(x => x.PickedQuantity > 0 && x.SourceLocationId.HasValue)
            .Select(x => x.SourceLocationId!.Value)
            .Append(line.DefaultSourceLocationId ?? 0)
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        if (originalSources.Length == 1) return originalSources[0];
        return pickTaskLine?.SourceLocationId ?? line.DefaultSourceLocationId;
    }

    internal static (long StagingLocationId, long TargetLocationId) ResolveReturnTaskLineLocations(
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        WarehouseTransferTaskLine pickTaskLine)
    {
        var stagingLocationId = ResolveStagingLocationId(header, line, pickTaskLine)
            ?? throw AppException.Conflict("Kaynak depo için üretim transfer bekleme rafı tanımlanmamış.");
        var targetLocationId = ResolveReturnTargetLocationId(line, pickTaskLine)
            ?? throw AppException.Conflict($"{line.StockCodeSnapshot} için iade hedef rafı bulunamadı.");
        return (stagingLocationId, targetLocationId);
    }

    internal static async Task ApplySelectedTargetLocationsAsync(
        IUnitOfWork uow,
        WarehouseTransferTask task,
        IReadOnlyList<CompleteProductionReturnLineRequest> selections,
        long actor,
        CancellationToken ct)
    {
        if (selections.Count == 0)
            throw AppException.BadRequest("İade satırları için hedef raf seçimi zorunludur.");

        var activeLines = task.Lines.Where(x => !x.IsDeleted).ToArray();
        if (activeLines.Length == 0)
            throw AppException.BadRequest("İade edilecek satır bulunmuyor.");

        var openLines = activeLines
            .Where(x => x.ProcessedQuantity + 0.0001m < x.PlannedQuantity)
            .ToArray();
        if (openLines.Length == 0)
            throw AppException.BadRequest("İade edilecek açık satır bulunmuyor.");

        var isRackless = await ProductionTransferWarehouseRacklessSupport.IsRacklessAsync(
            uow, task.Header.SourceWarehouseId, ct);

        var selectionByTaskLine = selections
            .GroupBy(x => x.TaskLineId)
            .ToDictionary(x => x.Key, x => x.Last().TargetLocationId);

        if (!isRackless && selectionByTaskLine.Count != openLines.Length)
            throw AppException.BadRequest("Her açık iade satırı için hedef raf seçilmelidir.");

        foreach (var taskLine in openLines)
        {
            selectionByTaskLine.TryGetValue(taskLine.Id, out var targetLocationId);
            if (!isRackless && !selectionByTaskLine.ContainsKey(taskLine.Id))
                throw AppException.BadRequest($"{taskLine.Line.StockCodeSnapshot} için hedef raf seçilmedi.");
            await ApplyLineTargetLocationAsync(uow, task, taskLine, targetLocationId, actor, ct);
        }
    }

    internal static async Task ApplyLineTargetLocationAsync(
        IUnitOfWork uow,
        WarehouseTransferTask task,
        WarehouseTransferTaskLine taskLine,
        long targetLocationId,
        long actor,
        CancellationToken ct)
    {
        if (targetLocationId <= 0)
        {
            targetLocationId = await ProductionTransferWarehouseRacklessSupport.GetRacklessTargetLocationIdAsync(
                    uow, task.Header.SourceWarehouseId, ct)
                ?? throw AppException.BadRequest($"{taskLine.Line.StockCodeSnapshot} için hedef raf seçilmedi.");
        }

        var location = await uow.Repository<WarehouseLocation>().Query()
            .AnyAsync(x => x.Id == targetLocationId
                && x.WarehouseId == task.Header.SourceWarehouseId
                && x.IsActive
                && x.IsPickable, ct);
        if (!location)
            throw AppException.BadRequest($"{taskLine.Line.StockCodeSnapshot} için seçilen raf bulunamadı, aktif değil veya toplanabilir değil.");

        var stagingLocationId = taskLine.SourceLocationId
            ?? ResolveStagingLocationId(task.Header, taskLine.Line)
            ?? task.Header.SourceStagingLocationId;
        if (stagingLocationId.HasValue && targetLocationId == stagingLocationId.Value)
            throw AppException.BadRequest($"{taskLine.Line.StockCodeSnapshot} için hedef raf bekleme rafı olamaz.");

        taskLine.TargetLocationId = targetLocationId;
        taskLine.UpdatedBy = actor;
        taskLine.UpdatedDate = DateTime.UtcNow;
    }

    internal static void ApplyReturnedRouteLocations(WarehouseTransferTask task, long actor)
    {
        var utcNow = DateTime.UtcNow;
        foreach (var taskLine in task.Lines.Where(x => !x.IsDeleted && x.TargetLocationId.HasValue))
        {
            var line = taskLine.Line;
            line.DefaultSourceLocationId = taskLine.TargetLocationId;
            line.UpdatedBy = actor;
            line.UpdatedDate = utcNow;
            foreach (var tracking in line.Trackings.Where(x => x.PickedQuantity > 0))
            {
                tracking.SourceLocationId = taskLine.TargetLocationId;
                tracking.UpdatedBy = actor;
                tracking.UpdatedDate = utcNow;
            }
        }
    }

    internal static IReadOnlyList<StockMovementLineRequest> BuildMovementLines(
        WarehouseTransferTask task,
        WarehouseTransferTaskLine? onlyTaskLine = null)
    {
        var header = task.Header;
        var rows = new List<StockMovementLineRequest>();
        foreach (var taskLine in task.Lines.Where(x => !x.IsDeleted))
        {
            if (onlyTaskLine is not null && taskLine.Id != onlyTaskLine.Id) continue;

            var remainingQuantity = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
            if (onlyTaskLine is null && remainingQuantity <= 0) continue;
            if (onlyTaskLine is not null && taskLine.ProcessedQuantity >= taskLine.PlannedQuantity) continue;

            var line = taskLine.Line;
            var defaultStaging = taskLine.SourceLocationId
                ?? ResolveStagingLocationId(header, line)
                ?? header.SourceStagingLocationId
                ?? throw AppException.Conflict("Kaynak depo için üretim transfer bekleme rafı tanımlanmamış.");
            var defaultTarget = taskLine.TargetLocationId
                ?? ResolveReturnTargetLocationId(line)
                ?? throw AppException.Conflict($"{line.StockCodeSnapshot} için iade hedef rafı bulunamadı.");

            var tracked = line.Trackings.Where(x => x.PickedQuantity > 0).ToList();
            if (tracked.Count == 0)
            {
                var quantity = onlyTaskLine is not null ? remainingQuantity : taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
                if (quantity <= 0) continue;
                if (defaultStaging != defaultTarget)
                {
                    rows.Add(new(
                        line.StockId, line.YapCodeId, quantity,
                        header.SourceWarehouseId, defaultStaging, header.SourceWarehouseId, defaultTarget,
                        line.UnitCode, null, null, null, line.SourceStockStatus, line.SourceStockStatus));
                }
                continue;
            }

            foreach (var tracking in tracked)
            {
                var source = tracking.TargetLocationId ?? defaultStaging;
                var target = taskLine.TargetLocationId ?? tracking.SourceLocationId ?? defaultTarget;
                if (source == target) continue;
                rows.Add(new(
                    line.StockId, line.YapCodeId, tracking.PickedQuantity,
                    header.SourceWarehouseId, source, header.SourceWarehouseId, target,
                    line.UnitCode, tracking.LotNo, tracking.SerialNo, null, line.SourceStockStatus, line.SourceStockStatus));
            }
        }

        return rows;
    }
}
