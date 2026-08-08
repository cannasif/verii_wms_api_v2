using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal sealed record PickBalanceContext(
    HashSet<long> ExcludedLocationIds,
    Dictionary<long, WarehouseLocation> Locations,
    List<LocationStockBalance> Balances);

internal static class ProductionTransferPickingSupport
{
    internal static WarehouseTransferTask ResolveWorkerPickTask(WarehouseTransferHeader header, long actor)
    {
        var tasks = header.Tasks
            .Where(x => x.TaskType == WarehouseTransferTaskType.Pick
                && x.Status is not (WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled))
            .OrderByDescending(x => x.Id)
            .ToArray();
        var assigned = tasks.FirstOrDefault(x => x.Assignments.Any(a => !a.IsDeleted && a.UserId == actor));
        return assigned ?? tasks.FirstOrDefault()
            ?? throw AppException.Conflict("Bu transfer için aktif toplama görevi bulunamadı.");
    }

    internal static async Task<PickBalanceContext> LoadBalanceContextAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        IEnumerable<WarehouseTransferLine> lines,
        CancellationToken ct)
    {
        var excluded = await ProductionTransferSourceLocationExclusions.FromHeaderAsync(uow, header, lines, ct);
        var locations = await uow.Repository<WarehouseLocation>().Query()
            .Where(x => x.WarehouseId == header.SourceWarehouseId && x.IsActive && x.IsPickable && !x.IsQuarantine)
            .ToDictionaryAsync(x => x.Id, ct);
        var stockIds = lines.Select(x => x.StockId).Distinct().ToArray();
        var balances = (await uow.Repository<LocationStockBalance>().Query()
            .Where(x => x.WarehouseId == header.SourceWarehouseId
                && stockIds.Contains(x.StockId)
                && locations.Keys.Contains(x.LocationId)
                && x.StockStatus == "Available"
                && x.AvailableQuantity > 0)
            .ToListAsync(ct))
            .Where(x => !excluded.Contains(x.LocationId))
            .ToList();
        return new(excluded, locations, balances);
    }

    internal static WarehouseTransferLine ResolveTaskLine(
        WarehouseTransferHeader header,
        WarehouseTransferTaskLine taskLine) =>
        taskLine.Line ?? header.Lines.Single(x => x.Id == taskLine.WtLineId);

    internal static void EnsureHeaderReleasedForPicking(
        WarehouseTransferHeader header,
        long actor,
        DateTimeOffset now)
    {
        if (header.Status != WarehouseTransferStatus.Draft) return;
        if (header.RequireApproval && header.ApprovalStatus != OperationApprovalStatus.Approved)
            throw AppException.Conflict("Transfer serbest bırakılmadan önce onaylanmalıdır.");

        header.Status = WarehouseTransferStatus.Released;
        header.ReleasedAtUtc = now;
        header.ReleasedBy = actor;
        header.UpdatedBy = actor;
        header.UpdatedDate = DateTime.UtcNow;

        foreach (var task in header.Tasks.Where(x =>
                     x.Status == WarehouseTransferTaskStatus.Open
                     && x.Assignments.Any(a => !a.IsDeleted)))
            task.Status = WarehouseTransferTaskStatus.Assigned;
    }

    internal static IReadOnlyList<ProductionTransferPickingRowDto> BuildPreviewRows(
        WarehouseTransferHeader header,
        WarehouseTransferTask task,
        PickBalanceContext context,
        Dictionary<long, string> locationCodes)
    {
        var rows = new List<ProductionTransferPickingRowDto>();
        foreach (var taskLine in task.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.Id))
        {
            var line = ResolveTaskLine(header, taskLine);
            var remaining = Math.Max(0, taskLine.PlannedQuantity - taskLine.ProcessedQuantity);
            if (remaining <= 0 && taskLine.ProcessedQuantity <= 0) continue;

            if (line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial)
            {
                foreach (var chunk in ProductionTransferRouteAllocation.BuildSerialPreviewRows(line, remaining))
                    rows.Add(ToRow(taskLine.Id, line, chunk, locationCodes, taskLine.ProcessedQuantity, preview: true));
                continue;
            }

            foreach (var chunk in ProductionTransferRouteAllocation.AllocateGreedyNonSerial(
                         remaining, line.StockId, line.YapCodeId, line.UnitCode, context.Balances, context.Locations))
                rows.Add(ToRow(taskLine.Id, line, chunk, locationCodes, taskLine.ProcessedQuantity, preview: true));
        }
        return rows;
    }

    internal static IReadOnlyList<ProductionTransferPickingRowDto> BuildPersistedRows(
        WarehouseTransferHeader header,
        WarehouseTransferTask task,
        Dictionary<long, string> locationCodes)
    {
        var rows = new List<ProductionTransferPickingRowDto>();
        foreach (var taskLine in task.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.Id))
        {
            var line = ResolveTaskLine(header, taskLine);
            var processed = Math.Max(taskLine.ProcessedQuantity, line.PickedQuantity);
            var remaining = Math.Max(0, taskLine.PlannedQuantity - processed);
            if (remaining <= 0 && processed <= 0) continue;

            if (line.Trackings.Count > 0)
            {
                foreach (var tracking in line.Trackings.OrderBy(x => x.Id))
                {
                    var trackingRemaining = tracking.PlannedQuantity - tracking.PickedQuantity;
                    if (trackingRemaining <= 0 && tracking.PickedQuantity <= 0) continue;
                    var locationId = tracking.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
                    rows.Add(new(
                        taskLine.Id, line.Id, line.LineNo, locationId,
                        locationId.HasValue ? locationCodes.GetValueOrDefault(locationId.Value) : null,
                        line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, tracking.SerialNo,
                        tracking.PlannedQuantity, Math.Max(0, trackingRemaining), tracking.PickedQuantity,
                        locationId.HasValue && trackingRemaining > 0));
                }
                continue;
            }

            var sourceLocationId = taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
            rows.Add(new(
                taskLine.Id, line.Id, line.LineNo, sourceLocationId,
                sourceLocationId.HasValue ? locationCodes.GetValueOrDefault(sourceLocationId.Value) : null,
                line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                taskLine.PlannedQuantity, Math.Max(0, taskLine.PlannedQuantity - processed), processed,
                sourceLocationId.HasValue && remaining > 0));
        }
        return rows;
    }

    internal static ProductionTransferPickingTableDto MapTable(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        bool isLocked,
        IReadOnlyList<ProductionTransferPickingRowDto> rows)
    {
        var requested = header.Lines.Sum(x => x.RequestedQuantity);
        var picked = header.Lines.Sum(x => x.PickedQuantity);
        return new(
            header.Id,
            header.DocumentNo,
            header.ExternalReferenceNo,
            link.WorkflowStatus,
            task.Id,
            task.TaskNo,
            isLocked,
            picked > 0 && link.WorkflowStatus is ProductionTransferWorkflowStatus.Planned or ProductionTransferWorkflowStatus.Picking,
            requested,
            picked,
            Math.Max(0, requested - picked),
            rows);
    }

    internal static async Task<Dictionary<long, string>> LoadLocationCodesAsync(
        IUnitOfWork uow,
        IEnumerable<long?> locationIds,
        CancellationToken ct)
    {
        var ids = locationIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await uow.Repository<WarehouseLocation>().Query()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);
    }

    private static ProductionTransferPickingRowDto ToRow(
        long taskLineId,
        WarehouseTransferLine line,
        RouteAllocationChunk chunk,
        Dictionary<long, string> locationCodes,
        decimal processedQuantity,
        bool preview)
    {
        var canPick = chunk.LocationId.HasValue && chunk.Quantity > 0;
        return new(
            taskLineId,
            line.Id,
            line.LineNo,
            chunk.LocationId,
            chunk.LocationId.HasValue ? locationCodes.GetValueOrDefault(chunk.LocationId.Value) : null,
            line.StockId,
            line.StockCodeSnapshot,
            line.StockNameSnapshot,
            chunk.SerialNo,
            chunk.Quantity,
            chunk.Quantity,
            preview ? 0 : processedQuantity,
            canPick);
    }
}
