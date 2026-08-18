using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

/// <summary>
/// Rafsız: bakiyeli / bakiyesiz ayrımı raflı rota bölmesi gibi kalıcı satır olur.
/// Raflı depolara dokunmaz.
/// </summary>
internal static class ProductionTransferRacklessBalanceSplitSupport
{
    internal static IReadOnlyList<RouteAllocationChunk> AllocateNonSerial(
        decimal needed,
        long locationId,
        decimal availablePool,
        decimal lineReserved)
    {
        if (needed <= 0) return [];
        var take = Math.Min(needed, Math.Max(0, availablePool) + Math.Max(0, lineReserved));
        var chunks = new List<RouteAllocationChunk>(2);
        if (take > 0)
            chunks.Add(new(locationId, take, null, null));
        var shortage = needed - take;
        if (shortage > 0)
            chunks.Add(new(null, shortage, null, null));
        return chunks;
    }

    internal static decimal ConsumePool(decimal availablePool, decimal take, decimal lineReserved) =>
        Math.Max(0, availablePool - Math.Max(0, take - Math.Max(0, lineReserved)));

    internal static async Task ApplyAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        long actor,
        long? stockIdFilter,
        CancellationToken ct)
    {
        if (!await ProductionTransferWarehouseRacklessSupport.IsRacklessAsync(uow, header.SourceWarehouseId, ct))
            return;
        if (task.TaskType is WarehouseTransferTaskType.CancellationReturn or WarehouseTransferTaskType.AssignmentReturn)
            return;
        if (task.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
            return;

        var locationId = await ProductionTransferWarehouseRacklessSupport.GetRacklessTargetLocationIdAsync(
            uow, header.SourceWarehouseId, ct);
        if (!locationId.HasValue) return;

        var movable = task.Lines
            .Where(x => !x.IsDeleted && x.PlannedQuantity - x.ProcessedQuantity > 0)
            .Select(taskLine =>
            {
                var line = ProductionTransferPickingSupport.ResolveTaskLine(header, taskLine);
                return (TaskLine: taskLine, Line: line);
            })
            .Where(x => x.Line.Trackings.Count == 0)
            .Where(x => !stockIdFilter.HasValue || x.Line.StockId == stockIdFilter.Value)
            .OrderBy(x => HasSourceLocation(x.TaskLine, x.Line) ? 0 : 1)
            .ThenBy(x => x.TaskLine.Id)
            .ToArray();
        if (movable.Length == 0) return;

        var stockIds = movable.Select(x => x.Line.StockId).Distinct().ToArray();
        var balances = await uow.Repository<LocationStockBalance>().Query()
            .Where(x => x.WarehouseId == header.SourceWarehouseId
                && x.LocationId == locationId.Value
                && stockIds.Contains(x.StockId)
                && x.StockStatus == "Available"
                && x.Quantity > 0)
            .ToListAsync(ct);
        var pools = balances
            .GroupBy(x => (x.StockId, x.YapCodeId, Unit: x.UnitCode.Trim().ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AvailableQuantity));

        var utcNow = DateTime.UtcNow;
        var nextLineNo = await ProductionTransferLineSplitHelper.ResolveNextLineNoAnchorAsync(uow, header, ct);

        foreach (var (taskLine, line) in movable)
        {
            var remaining = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
            if (remaining <= 0) continue;

            var poolKey = (line.StockId, line.YapCodeId, Unit: line.UnitCode.Trim().ToUpperInvariant());
            var pool = pools.GetValueOrDefault(poolKey);
            var reserved = ProductionTransferPickingBalanceSupport.ResolvePickableQuantity(
                line, locationId.Value, reservedOnly: true);
            var chunks = AllocateNonSerial(remaining, locationId.Value, pool, reserved);
            if (chunks.Count == 0) continue;

            var take = chunks.Where(x => x.LocationId.HasValue).Sum(x => x.Quantity);
            pools[poolKey] = ConsumePool(pool, take, reserved);

            var sourceLineLink = link.Lines.Single(x => x.WarehouseTransferLineId == line.Id && !x.IsDeleted);
            if (chunks.All(x => !x.LocationId.HasValue) && taskLine.ProcessedQuantity <= 0)
            {
                line.DefaultSourceLocationId = null;
                taskLine.SourceLocationId = null;
                line.RequestedQuantity = remaining;
                taskLine.PlannedQuantity = remaining;
                sourceLineLink.RequiredQuantity = remaining;
                continue;
            }

            ProductionTransferLineSplitHelper.ApplyNonSerialRouteChunks(
                header, link, task, taskLine, line, sourceLineLink, chunks, ref nextLineNo, actor, utcNow,
                allowShortageWithoutLocation: true);
        }

        ProductionTransferLineSplitHelper.RemoveRedundantShortageSiblings(header, task, link);
        ProductionTransferLineSplitHelper.ConsolidateSameLocationOpenTaskLines(header, link, task, actor, utcNow);
    }

    private static bool HasSourceLocation(WarehouseTransferTaskLine taskLine, WarehouseTransferLine line) =>
        (taskLine.SourceLocationId ?? line.DefaultSourceLocationId).HasValue;
}
