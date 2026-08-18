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

internal sealed record AssignedPickActionContext(
    WarehouseTransferTask SourceTask,
    WarehouseTransferTask ActiveTask)
{
    internal bool IsTransferred => SourceTask.Id != ActiveTask.Id;
}

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

    internal static AssignedPickActionContext ResolveAssignedPickActionForLine(
        WarehouseTransferHeader header,
        long taskLineId,
        long actor)
    {
        var sourceTask = header.Tasks.SingleOrDefault(x =>
            x.TaskType == WarehouseTransferTaskType.Pick
            && x.Lines.Any(line => line.Id == taskLineId && !line.IsDeleted))
            ?? throw AppException.BadRequest("Toplama satırı bu üretim transferine ait değil.");
        var activeTask = ResolveWorkerPickTask(header, actor);
        if (activeTask.Status is not (WarehouseTransferTaskStatus.InProgress or WarehouseTransferTaskStatus.PartiallyCompleted))
            throw AppException.Conflict("Toplanan stok işlemleri yalnızca başlatılmış görevinizde yapılabilir.");

        var lineage = ResolveTaskLineage(header, activeTask);
        if (!lineage.Any(x => x.Id == sourceTask.Id))
            throw AppException.Forbidden("Bu toplama satırı aktif görevinize ait değil veya başka bir görev zincirinde bulunuyor.");

        return new(sourceTask, activeTask);
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
        foreach (var taskLine in task.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.Id))
        {
            var line = ResolveTaskLine(header, taskLine);
            var processed = taskLine.ProcessedQuantity;
            var remaining = Math.Max(0, taskLine.PlannedQuantity - processed);
            if (remaining <= 0 && processed <= 0) continue;

            if (line.Trackings.Count > 0)
            {
                rows.Add(new(
                    taskLine.Id, line.Id, line.LineNo,
                    null, null,
                    line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                    taskLine.PlannedQuantity, remaining, processed,
                    false));
                continue;
            }

            if (processed > 0 && remaining > 0)
            {
                rows.Add(new(
                    taskLine.Id, line.Id, line.LineNo,
                    null, null,
                    line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                    processed, 0m, processed,
                    false));
                rows.Add(new(
                    taskLine.Id, line.Id, line.LineNo,
                    null, null,
                    line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                    remaining, remaining, 0m,
                    false));
                continue;
            }

            rows.Add(new(
                taskLine.Id, line.Id, line.LineNo,
                null, null,
                line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, null,
                taskLine.PlannedQuantity, remaining, processed,
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

                var pickedSerialOffset = GetTaskLinePickedSerialOffset(header, task, taskLine);
                foreach (var (tracking, trackingProcessed, trackingRemaining) in EnumerateTaskScopedSerialTrackings(
                             line, taskLine, pickedSerialOffset))
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

    internal static IReadOnlyList<ProductionTransferPickingRowDto> BuildHistoricalPickedRows(
        WarehouseTransferHeader header,
        WarehouseTransferTask currentTask,
        Dictionary<long, string> locationCodes)
    {
        var lineage = ResolveTaskLineage(header, currentTask);
        if (lineage.Count <= 1) return [];

        return lineage
            .Take(lineage.Count - 1)
            .Where(x => x.TaskType == WarehouseTransferTaskType.Pick)
            .SelectMany(task => BuildPersistedRows(header, task, locationCodes))
            .Where(row => row.ProcessedQuantity > 0)
            .Select(row => row with
            {
                RequestedQuantity = row.ProcessedQuantity,
                RemainingQuantity = 0,
                CanPick = false,
                IsHistorical = true
            })
            .ToArray();
    }

    internal static IReadOnlyList<WarehouseTransferTask> ResolveTaskLineage(
        WarehouseTransferHeader header,
        WarehouseTransferTask currentTask)
    {
        var tasksById = header.Tasks
            .Where(x => !x.IsDeleted && x.Id > 0)
            .GroupBy(x => x.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var lineage = new List<WarehouseTransferTask>();
        var visited = new HashSet<long>();
        var cursor = currentTask;

        while (true)
        {
            if (cursor.Id > 0 && !visited.Add(cursor.Id))
                throw AppException.Conflict("Görev devir zincirinde döngü tespit edildi.");

            lineage.Add(cursor);
            if (!cursor.PreviousTaskId.HasValue) break;
            if (!tasksById.TryGetValue(cursor.PreviousTaskId.Value, out var previous)) break;
            cursor = previous;
        }

        lineage.Reverse();
        return lineage;
    }

    internal static IEnumerable<long?> CollectTaskLineageLocationIds(
        WarehouseTransferHeader header,
        WarehouseTransferTask currentTask) =>
        ResolveTaskLineage(header, currentTask)
            .SelectMany(task => task.Lines.Where(x => !x.IsDeleted))
            .SelectMany(taskLine =>
            {
                var line = ResolveTaskLine(header, taskLine);
                return new long?[] { taskLine.SourceLocationId, line.DefaultSourceLocationId }
                    .Concat(line.Trackings.Select(tracking => tracking.SourceLocationId));
            });

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
        => EnumerateTaskScopedSerialTrackings(line, taskLine, 0);

    private static IEnumerable<(WarehouseTransferTracking Tracking, decimal Processed, decimal Remaining)>
        EnumerateTaskScopedSerialTrackings(
            WarehouseTransferLine line,
            WarehouseTransferTaskLine taskLine,
            int pickedOffset)
    {
        var pickedBudget = (int)Math.Floor(taskLine.ProcessedQuantity);
        var openBudget = (int)Math.Ceiling(Math.Max(0, taskLine.PlannedQuantity - taskLine.ProcessedQuantity));

        foreach (var tracking in line.Trackings.OrderBy(x => x.Id))
        {
            if (tracking.PickedQuantity > 0)
            {
                if (pickedOffset > 0)
                {
                    pickedOffset--;
                    continue;
                }
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
        WarehouseTransferHeader header,
        WarehouseTransferTask task,
        WarehouseTransferLine line,
        WarehouseTransferTaskLine taskLine)
    {
        var pickedBudget = (int)Math.Floor(taskLine.ProcessedQuantity);
        if (pickedBudget <= 0) return [];
        var pickedOffset = GetTaskLinePickedSerialOffset(header, task, taskLine);

        return line.Trackings
            .Where(t => t.PickedQuantity > 0 && !string.IsNullOrWhiteSpace(t.SerialNo))
            .OrderBy(t => t.Id)
            .Skip(pickedOffset)
            .Take(pickedBudget)
            .Select(t => t.SerialNo!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetTaskLinePickedSerialOffset(
        WarehouseTransferHeader header,
        WarehouseTransferTask task,
        WarehouseTransferTaskLine taskLine)
    {
        var lineage = ResolveTaskLineage(header, task);
        decimal processedBefore = 0;
        foreach (var lineageTask in lineage)
        {
            if (lineageTask.Id == task.Id)
            {
                processedBefore += lineageTask.Lines
                    .Where(x => !x.IsDeleted && x.WtLineId == taskLine.WtLineId && x.Id < taskLine.Id)
                    .Sum(x => x.ProcessedQuantity);
                break;
            }

            processedBefore += lineageTask.Lines
                .Where(x => !x.IsDeleted && x.WtLineId == taskLine.WtLineId)
                .Sum(x => x.ProcessedQuantity);
        }

        return Math.Max(0, (int)Math.Floor(processedBefore));
    }

    internal static async Task<PickBalanceContext> LoadSerialRouteRefreshBalanceContextAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        CancellationToken ct)
    {
        // Seri rotası: kaynak depodaki tüm uygun serili bakiyeler; yalnızca hedef raf dışlaması çağıran tarafta uygulanır.
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
        var locationCodes = await LoadLocationCodesAsync(
            uow, CollectTaskLineageLocationIds(header, task), ct);
        var currentRows = isLocked
            ? BuildRecipeRows(header, task)
            : BuildPersistedRows(header, task, locationCodes);
        var rows = currentRows
            .Concat(BuildHistoricalPickedRows(header, task, locationCodes))
            .ToArray();

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
