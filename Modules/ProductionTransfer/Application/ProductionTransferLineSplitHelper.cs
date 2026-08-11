using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferLineSplitHelper
{
    internal static bool IsSerialTrackedLine(WarehouseTransferLine line) =>
        line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial
        || line.RequireSerial;

    internal static bool IsSerialShortageTracking(WarehouseTransferTracking tracking) =>
        string.IsNullOrWhiteSpace(tracking.SerialNo);

    internal static LocationStockBalance? FindSerialTrackingBalance(
        WarehouseTransferLine line,
        WarehouseTransferTracking tracking,
        IEnumerable<LocationStockBalance> balances,
        IReadOnlyDictionary<long, WarehouseLocation> locations)
    {
        if (IsSerialShortageTracking(tracking)) return null;

        return balances
            .Where(x => x.StockId == line.StockId
                && x.YapCodeId == line.YapCodeId
                && string.Equals(x.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.SerialNo)
                && string.Equals(x.SerialNo.Trim(), tracking.SerialNo!.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.LotNo ?? string.Empty, tracking.LotNo ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.AvailableQuantity)
            .ThenBy(x => locations[x.LocationId].Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
    internal static void ApplySerialShortageRouteChunks(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        ProductionTransferLineLink sourceLineLink,
        IReadOnlyList<RouteAllocationChunk> chunks,
        ref int nextLineNo,
        long actor,
        DateTime utcNow)
    {
        var located = chunks.Where(x => x.Quantity > 0 && x.LocationId.HasValue).ToArray();
        var total = located.Sum(x => x.Quantity);
        if (total <= 0) return;

        var toConsume = total;
        foreach (var tracking in line.Trackings
                     .Where(x => !x.IsDeleted && x.PickedQuantity <= 0 && IsSerialShortageTracking(x))
                     .OrderBy(x => x.Id)
                     .ToArray())
        {
            var open = tracking.PlannedQuantity - tracking.PickedQuantity;
            if (open <= 0) continue;
            var take = Math.Min(open, toConsume);
            tracking.PlannedQuantity -= take;
            tracking.UpdatedBy = actor;
            tracking.UpdatedDate = utcNow;
            if (tracking.PlannedQuantity <= 0 && tracking.PickedQuantity <= 0 && tracking.ReservedQuantity <= 0)
            {
                tracking.IsDeleted = true;
                tracking.DeletedDate = utcNow;
                tracking.DeletedBy = actor;
            }

            toConsume -= take;
            if (toConsume <= 0) break;
        }

        foreach (var chunk in located)
            AddSibling(header, link, task, taskLine, line, sourceLineLink, chunk, ref nextLineNo, actor, utcNow);

        var serialTrackings = line.Trackings.Where(x => !x.IsDeleted && !IsSerialShortageTracking(x)).ToArray();
        var serialOpen = serialTrackings.Sum(x => Math.Max(0, x.PlannedQuantity - x.PickedQuantity));
        var serialProcessed = serialTrackings.Sum(x => x.PickedQuantity);
        var openQty = serialOpen + serialProcessed;
        line.RequestedQuantity = openQty;
        taskLine.PlannedQuantity = openQty;
        sourceLineLink.RequiredQuantity = openQty;
        line.UpdatedBy = actor;
        line.UpdatedDate = utcNow;
        taskLine.UpdatedBy = actor;
        taskLine.UpdatedDate = utcNow;

        var trackingLocations = serialTrackings
            .Where(x => x.PickedQuantity <= 0 && x.PlannedQuantity - x.PickedQuantity > 0)
            .Select(x => x.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        if (trackingLocations.Length == 1)
        {
            taskLine.SourceLocationId = trackingLocations[0];
            line.DefaultSourceLocationId = trackingLocations[0];
        }
        else if (trackingLocations.Length > 1)
        {
            taskLine.SourceLocationId = null;
            line.DefaultSourceLocationId = null;
        }
    }

    internal static void ApplyNonSerialRouteChunks(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        ProductionTransferLineLink sourceLineLink,
        IReadOnlyList<RouteAllocationChunk> chunks,
        ref int nextLineNo,
        long actor,
        DateTime utcNow,
        bool allowShortageWithoutLocation = false)
    {
        var located = chunks.Where(x => x.Quantity > 0 && x.LocationId.HasValue).ToArray();
        var shortage = allowShortageWithoutLocation
            ? chunks.Where(x => x.Quantity > 0 && !x.LocationId.HasValue).Sum(x => x.Quantity)
            : 0;
        var processed = taskLine.ProcessedQuantity;

        if (processed > 0)
        {
            line.RequestedQuantity = processed;
            taskLine.PlannedQuantity = processed;
            sourceLineLink.RequiredQuantity = processed;
            foreach (var chunk in located)
                AddSibling(header, link, task, taskLine, line, sourceLineLink, chunk, ref nextLineNo, actor, utcNow);
            if (shortage > 0)
                AddSibling(header, link, task, taskLine, line, sourceLineLink, new(null, shortage, null, null), ref nextLineNo, actor, utcNow);
            return;
        }

        if (located.Length == 0 && shortage <= 0)
        {
            line.DefaultSourceLocationId = null;
            taskLine.SourceLocationId = null;
            return;
        }

        if (located.Length == 0 && shortage > 0 && processed == 0)
        {
            line.DefaultSourceLocationId = null;
            taskLine.SourceLocationId = null;
            return;
        }

        if (located.Length > 0)
        {
            var first = located[0];
            line.RequestedQuantity = first.Quantity;
            line.DefaultSourceLocationId = first.LocationId;
            taskLine.PlannedQuantity = first.Quantity;
            taskLine.SourceLocationId = first.LocationId;
            sourceLineLink.RequiredQuantity = first.Quantity;
            foreach (var chunk in located.Skip(1))
                AddSibling(header, link, task, taskLine, line, sourceLineLink, chunk, ref nextLineNo, actor, utcNow);
        }
        else
        {
            line.DefaultSourceLocationId = null;
            taskLine.SourceLocationId = null;
        }

        if (shortage > 0)
            AddSibling(header, link, task, taskLine, line, sourceLineLink, new(null, shortage, null, null), ref nextLineNo, actor, utcNow);
    }

    internal static void ApplyPartialUnpickSplit(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        ProductionTransferLineLink lineLink,
        decimal unpickedQuantity,
        long targetLocationId,
        ref int nextLineNo,
        long actor,
        DateTime utcNow)
    {
        var stillPicked = taskLine.ProcessedQuantity;
        if (unpickedQuantity <= 0 || stillPicked <= 0) return;

        line.RequestedQuantity = stillPicked;
        taskLine.PlannedQuantity = stillPicked;
        lineLink.RequiredQuantity = stillPicked;
        line.UpdatedBy = actor;
        line.UpdatedDate = utcNow;
        taskLine.UpdatedBy = actor;
        taskLine.UpdatedDate = utcNow;

        AddSibling(
            header,
            link,
            task,
            taskLine,
            line,
            lineLink,
            new RouteAllocationChunk(targetLocationId, unpickedQuantity, null, null),
            ref nextLineNo,
            actor,
            utcNow);
    }

    internal static void ConsolidateSameLocationOpenTaskLines(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        long actor,
        DateTime utcNow)
    {
        var linkByWtLineId = link.Lines
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.WarehouseTransferLineId)
            .ToDictionary(x => x.Key, x => x.First());
        var candidates = task.Lines
            .Where(taskLine => !taskLine.IsDeleted && taskLine.PlannedQuantity - taskLine.ProcessedQuantity > 0)
            .Select(taskLine =>
            {
                var line = taskLine.Line ?? header.Lines.SingleOrDefault(x => x.Id == taskLine.WtLineId);
                if (line is null || line.IsDeleted || line.Trackings.Count > 0) return null;
                if (line.PickedQuantity > 0 || taskLine.ProcessedQuantity > 0) return null;
                if (!linkByWtLineId.TryGetValue(line.Id, out var lineLink)) return null;
                var locationId = taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
                if (!locationId.HasValue) return null;
                var groupKey = ProductionTransferRouteAllocation.BuildRouteSplitGroupKey(lineLink, line);
                return new MergeCandidate(taskLine, line, lineLink, locationId.Value, groupKey);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => (x.GroupKey, x.LocationId))
            .Where(group => group.Count() > 1);

        foreach (var group in candidates)
        {
            var members = group.OrderBy(x => x.TaskLine.Id).ToArray();
            var keeper = members[0];
            for (var index = 1; index < members.Length; index++)
            {
                var mergee = members[index];
                var openQuantity = mergee.TaskLine.PlannedQuantity - mergee.TaskLine.ProcessedQuantity;
                if (openQuantity <= 0) continue;

                keeper.TaskLine.PlannedQuantity += openQuantity;
                keeper.Line.RequestedQuantity += openQuantity;
                keeper.LineLink.RequiredQuantity += openQuantity;
                keeper.TaskLine.UpdatedBy = actor;
                keeper.TaskLine.UpdatedDate = utcNow;
                keeper.Line.UpdatedBy = actor;
                keeper.Line.UpdatedDate = utcNow;

                mergee.TaskLine.IsDeleted = true;
                mergee.TaskLine.DeletedDate = utcNow;
                mergee.TaskLine.DeletedBy = actor;

                if (mergee.Line.PickedQuantity <= 0)
                {
                    mergee.Line.IsDeleted = true;
                    mergee.Line.DeletedDate = utcNow;
                    mergee.Line.DeletedBy = actor;
                    mergee.LineLink.IsDeleted = true;
                    mergee.LineLink.DeletedDate = utcNow;
                    mergee.LineLink.DeletedBy = actor;
                }
            }
        }
    }

    internal static void RemoveRedundantShortageSiblings(
        WarehouseTransferHeader header,
        WarehouseTransferTask task,
        ProductionTransferHeaderLink link)
    {
        var removableLineIds = header.Lines
            .Where(line => !line.DefaultSourceLocationId.HasValue
                && line.PickedQuantity <= 0
                && line.ReservedQuantity <= 0
                && line.RequestedQuantity > 0)
            .GroupBy(line => (line.StockId, line.YapCodeId, line.UnitCode))
            .SelectMany(group =>
            {
                var ordered = group.OrderBy(line => line.LineNo).ToArray();
                if (ordered.Length <= 1) return [];
                var keeper = ordered[0];
                return ordered.Skip(1)
                    .Where(line => line.RequestedQuantity == keeper.RequestedQuantity)
                    .Select(line => line.Id);
            })
            .ToHashSet();
        if (removableLineIds.Count == 0) return;

        var utcNow = DateTime.UtcNow;
        foreach (var taskLine in task.Lines.Where(x => removableLineIds.Contains(x.WtLineId)))
        {
            taskLine.IsDeleted = true;
            taskLine.DeletedDate = utcNow;
        }

        foreach (var line in header.Lines.Where(x => removableLineIds.Contains(x.Id)))
        {
            line.IsDeleted = true;
            line.DeletedDate = utcNow;
        }

        foreach (var lineLink in link.Lines.Where(x => !x.IsDeleted && removableLineIds.Contains(x.WarehouseTransferLineId)))
        {
            lineLink.IsDeleted = true;
            lineLink.DeletedDate = utcNow;
        }
    }

    internal static void AssignSerialToShortage(
        WarehouseTransferLine line,
        WarehouseTransferTaskLine taskLine,
        long locationId,
        string serialNo,
        string? lotNo,
        long actor,
        DateTime utcNow)
    {
        var shortage = line.Trackings
            .Where(x => !x.IsDeleted && x.PickedQuantity <= 0 && IsSerialShortageTracking(x))
            .OrderBy(x => x.Id)
            .First(x => x.PlannedQuantity - x.PickedQuantity > 0);
        var open = shortage.PlannedQuantity - shortage.PickedQuantity;

        if (open <= 1)
        {
            shortage.SerialNo = serialNo.Trim();
            shortage.LotNo = lotNo;
            shortage.SourceLocationId = locationId;
            shortage.UpdatedBy = actor;
            shortage.UpdatedDate = utcNow;
        }
        else
        {
            shortage.PlannedQuantity -= 1;
            shortage.UpdatedBy = actor;
            shortage.UpdatedDate = utcNow;
            line.Trackings.Add(new WarehouseTransferTracking
            {
                BranchCode = line.BranchCode,
                CreatedBy = actor,
                CreatedDate = utcNow,
                Line = line,
                WtLineId = line.Id,
                LotNo = lotNo,
                SerialNo = serialNo.Trim(),
                PlannedQuantity = 1,
                SourceLocationId = locationId,
                Status = WarehouseTransferTrackingStatus.Planned,
            });
        }

        RefreshSerialLineLocations(taskLine, line);
    }

    private static void RefreshSerialLineLocations(WarehouseTransferTaskLine taskLine, WarehouseTransferLine line)
    {
        var openLocations = line.Trackings
            .Where(x => !x.IsDeleted
                && !IsSerialShortageTracking(x)
                && x.PickedQuantity <= 0
                && x.PlannedQuantity - x.PickedQuantity > 0)
            .Select(x => x.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (openLocations.Length == 1)
        {
            taskLine.SourceLocationId = openLocations[0];
            line.DefaultSourceLocationId = openLocations[0];
        }
        else if (openLocations.Length > 1)
        {
            taskLine.SourceLocationId = null;
            line.DefaultSourceLocationId = null;
        }
    }

    internal static void ApplySerialRouteReplacement(
        WarehouseTransferTracking tracking,
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        long locationId,
        string serialNo,
        string? lotNo,
        long actor,
        DateTime utcNow)
    {
        tracking.SerialNo = serialNo.Trim();
        tracking.LotNo = lotNo;
        tracking.SourceLocationId = locationId;
        tracking.UpdatedBy = actor;
        tracking.UpdatedDate = utcNow;

        RefreshSerialLineLocations(taskLine, line);
    }

    internal static void RefreshSerialSources(
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        PickBalanceContext context,
        long actor,
        DateTime utcNow)
    {
        var trackingLocations = new HashSet<long>();
        foreach (var tracking in line.Trackings.Where(x => x.PickedQuantity == 0))
        {
            if (IsSerialShortageTracking(tracking))
            {
                if (!tracking.SourceLocationId.HasValue) continue;
                tracking.SourceLocationId = null;
                tracking.UpdatedBy = actor;
                tracking.UpdatedDate = utcNow;
                continue;
            }

            var best = FindSerialTrackingBalance(line, tracking, context.Balances, context.Locations);
            if (best is null) continue;
            trackingLocations.Add(best.LocationId);
            if (tracking.SourceLocationId == best.LocationId) continue;
            tracking.SourceLocationId = best.LocationId;
            tracking.UpdatedBy = actor;
            tracking.UpdatedDate = utcNow;
        }

        if (trackingLocations.Count == 1)
        {
            taskLine.SourceLocationId = trackingLocations.Single();
            line.DefaultSourceLocationId = taskLine.SourceLocationId;
        }
        else if (trackingLocations.Count > 1)
        {
            taskLine.SourceLocationId = null;
            line.DefaultSourceLocationId = null;
        }
    }

    private static void AddSibling(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask task,
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        ProductionTransferLineLink sourceLineLink,
        RouteAllocationChunk chunk,
        ref int nextLineNo,
        long actor,
        DateTime utcNow)
    {
        var sibling = CloneLine(header, line, ++nextLineNo, chunk.Quantity, chunk.LocationId, actor, utcNow);
        link.Lines.Add(CloneLineLink(link, sourceLineLink, sibling, chunk.Quantity, actor, utcNow));
        task.Lines.Add(new WarehouseTransferTaskLine
        {
            BranchCode = task.BranchCode,
            CreatedBy = actor,
            CreatedDate = utcNow,
            Task = task,
            Line = sibling,
            PlannedQuantity = chunk.Quantity,
            ProcessedQuantity = 0,
            SourceLocationId = chunk.LocationId,
            TargetLocationId = taskLine.TargetLocationId
        });
    }

    private static WarehouseTransferLine CloneLine(
        WarehouseTransferHeader header,
        WarehouseTransferLine source,
        int lineNo,
        decimal quantity,
        long? sourceLocationId,
        long actor,
        DateTime utcNow)
    {
        var sibling = new WarehouseTransferLine
        {
            BranchCode = source.BranchCode,
            CreatedBy = actor,
            CreatedDate = utcNow,
            WtHeaderId = header.Id,
            Header = header,
            LineNo = lineNo,
            StockId = source.StockId,
            StockCodeSnapshot = source.StockCodeSnapshot,
            StockNameSnapshot = source.StockNameSnapshot,
            YapCodeId = source.YapCodeId,
            YapCodeSnapshot = source.YapCodeSnapshot,
            UnitCode = source.UnitCode,
            BaseUnitCode = source.BaseUnitCode,
            UnitConversionFactor = source.UnitConversionFactor,
            RequestedQuantity = quantity,
            TrackingType = source.TrackingType,
            RequireLot = source.RequireLot,
            RequireSerial = source.RequireSerial,
            RequireHandlingUnit = source.RequireHandlingUnit,
            SourceWarehouseId = source.SourceWarehouseId,
            TargetWarehouseId = source.TargetWarehouseId,
            DefaultSourceLocationId = sourceLocationId,
            DefaultTargetLocationId = source.DefaultTargetLocationId,
            SourceStockStatus = source.SourceStockStatus,
            TargetStockStatus = source.TargetStockStatus,
            Status = WarehouseTransferLineStatus.Open,
            Description = source.Description
        };
        header.Lines.Add(sibling);
        return sibling;
    }

    private static ProductionTransferLineLink CloneLineLink(
        ProductionTransferHeaderLink link,
        ProductionTransferLineLink source,
        WarehouseTransferLine sibling,
        decimal quantity,
        long actor,
        DateTime utcNow) =>
        new()
        {
            BranchCode = link.BranchCode,
            CreatedBy = actor,
            CreatedDate = utcNow,
            HeaderLink = link,
            WarehouseTransferLine = sibling,
            LineRole = source.LineRole,
            ProductionConsumptionId = source.ProductionConsumptionId,
            ProductionOutputId = source.ProductionOutputId,
            RequirementReference = source.RequirementReference,
            RequiredQuantity = quantity
        };

    private sealed record MergeCandidate(
        WarehouseTransferTaskLine TaskLine,
        WarehouseTransferLine Line,
        ProductionTransferLineLink LineLink,
        long LocationId,
        RouteSplitGroupKey GroupKey);
}
