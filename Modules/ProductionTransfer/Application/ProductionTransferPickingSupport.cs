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
        return assigned
            ?? throw AppException.Forbidden("Bu transfer için size atanmış aktif toplama görevi bulunamadı.");
    }

    internal static WarehouseTransferTask ResolveActivePickTaskForResume(
        WarehouseTransferHeader header,
        long actor)
    {
        var tasks = header.Tasks
            .Where(x => x.TaskType == WarehouseTransferTaskType.Pick
                && x.Status is not (WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled))
            .OrderByDescending(x => x.Id)
            .ToArray();
        return tasks.FirstOrDefault(x => x.Assignments.Any(a => !a.IsDeleted && a.UserId == actor))
            ?? tasks.FirstOrDefault(x => x.StartedBy == actor)
            ?? tasks.FirstOrDefault()
            ?? throw AppException.Conflict("Aktif toplama görevi bulunamadı.");
    }

    internal static WarehouseTransferTask ResolveAssignedPickTaskForLine(
        WarehouseTransferHeader header,
        long taskLineId,
        long actor)
    {
        var task = header.Tasks.SingleOrDefault(x =>
            x.TaskType == WarehouseTransferTaskType.Pick
            && x.Lines.Any(line => line.Id == taskLineId && !line.IsDeleted))
            ?? throw AppException.BadRequest("Beklenen toplama satırı bu üretim transferine ait değil.");
        if (!task.Assignments.Any(a => !a.IsDeleted && a.UserId == actor))
            throw AppException.Forbidden("Bu toplama görevi size atanmamış veya başka kullanıcıya devredilmiş.");
        if (task.Status is not (WarehouseTransferTaskStatus.InProgress or WarehouseTransferTaskStatus.PartiallyCompleted))
            throw AppException.Conflict("Toplama yalnızca başlatılmış görevinizde yapılabilir.");
        return task;
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

    internal static IReadOnlyList<ProductionTransferPickingRowDto> BuildRecipeRows(
        WarehouseTransferHeader header,
        WarehouseTransferTask task)
    {
        var rows = new List<ProductionTransferPickingRowDto>();
        foreach (var group in task.Lines
                     .Where(x => !x.IsDeleted)
                     .GroupBy(x => x.WtLineId)
                     .OrderBy(x => x.Min(line => line.Id)))
        {
            var anchorTaskLine = group.OrderBy(x => x.Id).First();
            var line = ResolveTaskLine(header, anchorTaskLine);
            var planned = group.Sum(x => x.PlannedQuantity);
            var processed = group.Sum(x => x.ProcessedQuantity);
            var remaining = Math.Max(0, planned - processed);
            if (remaining <= 0 && processed <= 0) continue;

            rows.Add(new(
                anchorTaskLine.Id,
                line.Id,
                line.LineNo,
                null,
                null,
                line.StockId,
                line.StockCodeSnapshot,
                line.StockNameSnapshot,
                null,
                planned,
                remaining,
                processed,
                false));
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
            var processed = taskLine.ProcessedQuantity;
            var remaining = Math.Max(0, taskLine.PlannedQuantity - processed);
            if (remaining <= 0 && processed <= 0) continue;

            if (line.Trackings.Count > 0)
            {
                decimal shortageRequested = 0;
                decimal shortageRemaining = 0;
                decimal shortageProcessed = 0;

                foreach (var (tracking, trackingProcessed, trackingRemaining) in EnumerateTaskScopedSerialTrackings(line, taskLine))
                {
                    if (ProductionTransferLineSplitHelper.IsSerialShortageTracking(tracking))
                    {
                        shortageRequested += trackingProcessed + trackingRemaining;
                        shortageRemaining += trackingRemaining;
                        shortageProcessed += trackingProcessed;
                        continue;
                    }

                    var locationId = tracking.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
                    rows.Add(new(
                        taskLine.Id, line.Id, line.LineNo, locationId,
                        locationId.HasValue ? locationCodes.GetValueOrDefault(locationId.Value) : null,
                        line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, tracking.SerialNo,
                        trackingProcessed + trackingRemaining, trackingRemaining, trackingProcessed,
                        locationId.HasValue && trackingRemaining > 0));
                }

                if (shortageRemaining > 0 || shortageProcessed > 0)
                {
                    rows.Add(new(
                        taskLine.Id, line.Id, line.LineNo,
                        null, null,
                        line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                        shortageRequested, shortageRemaining, shortageProcessed,
                        false));
                }

                continue;
            }

            var sourceLocationId = taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
            if (processed > 0 && remaining > 0)
            {
                rows.Add(new(
                    taskLine.Id, line.Id, line.LineNo, sourceLocationId,
                    sourceLocationId.HasValue ? locationCodes.GetValueOrDefault(sourceLocationId.Value) : null,
                    line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                    processed, 0m, processed,
                    false));
                rows.Add(new(
                    taskLine.Id, line.Id, line.LineNo, sourceLocationId,
                    sourceLocationId.HasValue ? locationCodes.GetValueOrDefault(sourceLocationId.Value) : null,
                    line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                    remaining, remaining, 0m,
                    sourceLocationId.HasValue && remaining > 0));
                continue;
            }

            rows.Add(new(
                taskLine.Id, line.Id, line.LineNo, sourceLocationId,
                sourceLocationId.HasValue ? locationCodes.GetValueOrDefault(sourceLocationId.Value) : null,
                line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                taskLine.PlannedQuantity, remaining, processed,
                sourceLocationId.HasValue && remaining > 0));
        }
        return rows;
    }

    // Serili satırlar transfer satırında paylaşıldığından, devredilen görevde yalnızca bu görevin
    // plan/processed miktarına denk gelen seriler gösterilir; önceki görevde toplanan seriler gizlenir.
    internal static decimal GetSerialShortageRemaining(
        WarehouseTransferLine line,
        WarehouseTransferTaskLine taskLine)
    {
        if (line.Trackings.Count == 0) return 0;
        return EnumerateTaskScopedSerialTrackings(line, taskLine)
            .Where(x => ProductionTransferLineSplitHelper.IsSerialShortageTracking(x.Tracking))
            .Sum(x => x.Remaining);
    }

    internal static IEnumerable<(WarehouseTransferTracking Tracking, decimal Processed, decimal Remaining)>
        EnumerateTaskScopedSerialTrackings(WarehouseTransferLine line, WarehouseTransferTaskLine taskLine)
    {
        var pickedBudget = (int)Math.Floor(taskLine.ProcessedQuantity);
        var openBudget = (int)Math.Ceiling(Math.Max(0, taskLine.PlannedQuantity - taskLine.ProcessedQuantity));

        foreach (var tracking in line.Trackings.OrderBy(x => x.Id))
        {
            if (tracking.PickedQuantity > 0)
            {
                if (pickedBudget <= 0) continue;
                pickedBudget--;
                yield return (tracking, tracking.PickedQuantity, 0m);
                continue;
            }

            var trackingRemaining = tracking.PlannedQuantity - tracking.PickedQuantity;
            if (trackingRemaining <= 0 || openBudget <= 0) continue;
            var remaining = Math.Min(trackingRemaining, openBudget);
            openBudget -= (int)Math.Ceiling(remaining);
            yield return (tracking, 0m, remaining);
        }
    }

    internal static IReadOnlyList<string> ResolveTaskScopedPickedSerialNos(
        WarehouseTransferLine line,
        WarehouseTransferTaskLine taskLine)
    {
        var pickedBudget = (int)Math.Floor(taskLine.ProcessedQuantity);
        if (pickedBudget <= 0) return [];

        return line.Trackings
            .Where(t => t.PickedQuantity > 0 && !string.IsNullOrWhiteSpace(t.SerialNo))
            .OrderBy(t => t.Id)
            .Take(pickedBudget)
            .Select(t => t.SerialNo!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static async Task<PickBalanceContext> LoadSerialRouteRefreshBalanceContextAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        CancellationToken ct)
    {
        // Seri rotası: kaynak depodaki tüm uygun serili bakiyeler; hedef raf vb. genel dışlamalar bu adımda uygulanmaz.
        var locations = await uow.Repository<WarehouseLocation>().Query()
            .Where(x => x.WarehouseId == header.SourceWarehouseId && x.IsActive && x.IsPickable && !x.IsQuarantine)
            .ToDictionaryAsync(x => x.Id, ct);
        var balances = (await uow.Repository<LocationStockBalance>().Query()
            .Where(x => x.WarehouseId == header.SourceWarehouseId
                && x.StockId == line.StockId
                && locations.Keys.Contains(x.LocationId)
                && x.StockStatus == "Available"
                && x.AvailableQuantity > 0
                && x.SerialNo != null && x.SerialNo != "")
            .ToListAsync(ct))
            .Where(x => string.Equals(x.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase)
                && x.YapCodeId == line.YapCodeId)
            .ToList();
        return new([], locations, balances);
    }

    internal static IReadOnlyList<ProductionTransferPickingRowDto> SortDisplayRows(
        IReadOnlyList<ProductionTransferPickingRowDto> rows,
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link)
    {
        var lineById = header.Lines.ToDictionary(x => x.Id);
        var linkByLineId = link.Lines.ToDictionary(x => x.WarehouseTransferLineId);
        var groupAnchorLineNo = new Dictionary<RouteSplitGroupKey, int>();

        foreach (var row in rows)
        {
            if (!lineById.TryGetValue(row.WtLineId, out var line)) continue;
            if (!linkByLineId.TryGetValue(row.WtLineId, out var lineLink)) continue;
            var key = ProductionTransferRouteAllocation.BuildRouteSplitGroupKey(lineLink, line);
            groupAnchorLineNo[key] = groupAnchorLineNo.TryGetValue(key, out var current)
                ? Math.Min(current, row.LineNo)
                : row.LineNo;
        }

        int AnchorLineNo(ProductionTransferPickingRowDto row)
        {
            if (!lineById.TryGetValue(row.WtLineId, out var line)) return row.LineNo;
            if (!linkByLineId.TryGetValue(row.WtLineId, out var lineLink)) return row.LineNo;
            var key = ProductionTransferRouteAllocation.BuildRouteSplitGroupKey(lineLink, line);
            return groupAnchorLineNo.GetValueOrDefault(key, row.LineNo);
        }

        return rows
            .OrderBy(AnchorLineNo)
            .ThenBy(x => x.LineNo)
            .ThenBy(x => x.TaskLineId)
            .ThenBy(x => x.SerialNo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceLocationCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static ProductionTransferPickingTableDto MapTable(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        bool isLocked,
        ProductionTransferPolicy policy,
        IReadOnlyList<ProductionTransferPickingRowDto> rows)
    {
        var requested = header.Lines.Sum(x => x.RequestedQuantity);
        var picked = header.Lines.Sum(x => x.PickedQuantity);
        var overIssueLines = ProductionTransferOverIssueSupport.BuildOverIssueLines(header.Lines);
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
            policy.AllowOverIssue,
            policy.OverIssueTolerancePercent,
            overIssueLines.Sum(x => x.OverIssueQuantity),
            overIssueLines,
            rows);
    }

    internal static async Task<ProductionTransferPickingTableDto> BuildInlinePickingTableAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        CancellationToken ct)
    {
        var policy = await ProductionTransferOverIssueSupport.LoadPolicyAsync(uow, header.BranchCode, ct);
        var isLocked = task.Status is not WarehouseTransferTaskStatus.InProgress
            and not WarehouseTransferTaskStatus.PartiallyCompleted;
        IReadOnlyList<ProductionTransferPickingRowDto> rows;
        if (isLocked)
            rows = BuildRecipeRows(header, task);
        else
        {
            var locationIds = task.Lines.SelectMany(x =>
            {
                var line = ResolveTaskLine(header, x);
                return new long?[] { x.SourceLocationId, line.DefaultSourceLocationId }
                    .Concat(line.Trackings.Select(t => t.SourceLocationId));
            });
            var locationCodes = await LoadLocationCodesAsync(uow, locationIds, ct);
            rows = BuildPersistedRows(header, task, locationCodes);
        }

        return MapTable(
            header, link, task, isLocked, policy,
            SortDisplayRows(rows, header, link));
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
}
