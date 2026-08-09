using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal sealed record RouteAllocationChunk(long? LocationId, decimal Quantity, string? SerialNo, string? LotNo);

internal readonly record struct RouteSplitGroupKey(
    long? ProductionConsumptionId,
    string RequirementReference,
    long StockId,
    long? YapCodeId,
    string UnitCode);

internal static class ProductionTransferRouteAllocation
{
    internal static IReadOnlyList<RouteAllocationChunk> AllocateGreedyNonSerial(
        decimal needed,
        long stockId,
        long? yapCodeId,
        string unitCode,
        IReadOnlyCollection<LocationStockBalance> balances,
        IReadOnlyDictionary<long, WarehouseLocation> locations)
    {
        if (needed <= 0) return [];
        var candidates = balances
            .Where(x => x.StockId == stockId
                && x.YapCodeId == yapCodeId
                && string.Equals(x.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(x.SerialNo))
            .GroupBy(x => x.LocationId)
            .Select(g => new { LocationId = g.Key, Available = g.Sum(x => x.AvailableQuantity) })
            .OrderBy(x => locations[x.LocationId].Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var remaining = needed;
        var result = new List<RouteAllocationChunk>();
        foreach (var candidate in candidates)
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, candidate.Available);
            if (take <= 0) continue;
            result.Add(new(candidate.LocationId, take, null, null));
            remaining -= take;
        }

        if (remaining > 0)
            result.Add(new(null, remaining, null, null));
        return result;
    }

    internal static string NormalizeSerial(string? serialNo) =>
        serialNo?.Trim().ToUpperInvariant() ?? string.Empty;

    internal static HashSet<string> GetAssignedSerialNumbersInGroup(
        WarehouseTransferTask task,
        WarehouseTransferLine line,
        ProductionTransferHeaderLink link,
        string? exceptSerialNo = null)
    {
        var lineLink = link.Lines.Single(x => x.WarehouseTransferLineId == line.Id);
        var groupKey = BuildRouteSplitGroupKey(lineLink, line);
        var except = NormalizeSerial(exceptSerialNo);
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var siblingTaskLine in task.Lines.Where(x => !x.IsDeleted))
        {
            var siblingLine = siblingTaskLine.Line;
            if (siblingLine is null) continue;
            var siblingLink = link.Lines.SingleOrDefault(x => x.WarehouseTransferLineId == siblingLine.Id);
            if (siblingLink is null) continue;
            if (BuildRouteSplitGroupKey(siblingLink, siblingLine) != groupKey) continue;

            foreach (var tracking in siblingLine.Trackings)
            {
                if (tracking.PlannedQuantity - tracking.PickedQuantity <= 0) continue;
                if (string.IsNullOrWhiteSpace(tracking.SerialNo)) continue;
                var normalized = NormalizeSerial(tracking.SerialNo);
                if (except.Length > 0 && string.Equals(normalized, except, StringComparison.OrdinalIgnoreCase)) continue;
                assigned.Add(normalized);
            }
        }

        return assigned;
    }

    internal static IReadOnlyList<(long LocationId, string SerialNo, string? LotNo, decimal AvailableQuantity)> ListSerialRouteRefreshCandidates(
        long stockId,
        long? yapCodeId,
        string unitCode,
        string currentSerialNo,
        IReadOnlySet<string> excludedSerialNumbers,
        IReadOnlyCollection<LocationStockBalance> balances,
        IReadOnlyDictionary<long, WarehouseLocation> locations)
    {
        var current = NormalizeSerial(currentSerialNo);
        return balances
            .Where(x => x.StockId == stockId
                && x.YapCodeId == yapCodeId
                && string.Equals(x.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.SerialNo)
                && !string.Equals(NormalizeSerial(x.SerialNo), current, StringComparison.OrdinalIgnoreCase)
                && !excludedSerialNumbers.Contains(NormalizeSerial(x.SerialNo))
                && x.AvailableQuantity > 0)
            .OrderBy(x => locations[x.LocationId].Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SerialNo, StringComparer.OrdinalIgnoreCase)
            .Select(x => (LocationId: x.LocationId, SerialNo: x.SerialNo!.Trim(), LotNo: (string?)x.LotNo, AvailableQuantity: x.AvailableQuantity))
            .ToArray();
    }

    internal static IReadOnlyList<(long LocationId, decimal AvailableQuantity)> ListNonSerialCandidates(
        long stockId,
        long? yapCodeId,
        string unitCode,
        IReadOnlyCollection<LocationStockBalance> balances,
        IReadOnlyDictionary<long, WarehouseLocation> locations)
    {
        return balances
            .Where(x => x.StockId == stockId
                && x.YapCodeId == yapCodeId
                && string.Equals(x.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(x.SerialNo))
            .GroupBy(x => x.LocationId)
            .Select(g => (LocationId: g.Key, AvailableQuantity: g.Sum(x => x.AvailableQuantity)))
            .Where(x => x.AvailableQuantity > 0)
            .OrderBy(x => locations[x.LocationId].Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static RouteSplitGroupKey BuildRouteSplitGroupKey(
        ProductionTransferLineLink lineLink,
        WarehouseTransferLine line) =>
        new(
            lineLink.ProductionConsumptionId,
            lineLink.RequirementReference?.Trim() ?? string.Empty,
            line.StockId,
            line.YapCodeId,
            line.UnitCode.Trim());

    internal static decimal GetRouteRefreshAvailableAtLocation(
        long locationId,
        long stockId,
        long? yapCodeId,
        string unitCode,
        IReadOnlyCollection<LocationStockBalance> balances,
        WarehouseTransferTask task,
        WarehouseTransferTaskLine currentTaskLine,
        WarehouseTransferLine line,
        ProductionTransferHeaderLink link,
        bool subtractSiblingCommitments)
    {
        var raw = balances
            .Where(x => x.LocationId == locationId
                && x.StockId == stockId
                && x.YapCodeId == yapCodeId
                && string.Equals(x.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(x.SerialNo))
            .Sum(x => x.AvailableQuantity);

        if (!subtractSiblingCommitments) return raw;

        var lineLink = link.Lines.Single(x => x.WarehouseTransferLineId == line.Id);
        var groupKey = BuildRouteSplitGroupKey(lineLink, line);
        var committed = 0m;
        foreach (var siblingTaskLine in task.Lines.Where(x => !x.IsDeleted && x.Id != currentTaskLine.Id))
        {
            var open = siblingTaskLine.PlannedQuantity - siblingTaskLine.ProcessedQuantity;
            if (open <= 0) continue;
            var siblingLine = siblingTaskLine.Line;
            if (siblingLine is null || siblingLine.Trackings.Count > 0) continue;
            var siblingLink = link.Lines.SingleOrDefault(x => x.WarehouseTransferLineId == siblingLine.Id);
            if (siblingLink is null) continue;
            if (BuildRouteSplitGroupKey(siblingLink, siblingLine) != groupKey) continue;

            var siblingLocation = siblingTaskLine.SourceLocationId ?? siblingLine.DefaultSourceLocationId;
            if (siblingLocation != locationId) continue;
            committed += open;
        }

        return Math.Max(0, raw - committed);
    }

    internal static HashSet<long> GetRouteRefreshExcludedSourceLocationIds(long? currentSourceLocationId) =>
        currentSourceLocationId.HasValue ? [currentSourceLocationId.Value] : [];

    /// <summary>
    /// Rota güncellemede kullanıcının seçtiği miktarlar kaynak rafın tamamını karşılamıyorsa,
    /// kalan miktarı mevcut kaynak rafa ilk parça olarak ekler; böylece orijinal satır orada kalır.
    /// </summary>
    internal static RouteAllocationChunk[] BuildRouteRefreshSplitChunks(
        decimal openQuantity,
        long? currentSourceLocationId,
        IEnumerable<RouteAllocationChunk> selectedSplits)
    {
        var selected = selectedSplits.Where(x => x.Quantity > 0 && x.LocationId.HasValue).ToArray();
        var routedTotal = selected.Sum(x => x.Quantity);
        var remainderOnSource = openQuantity - routedTotal;
        if (remainderOnSource <= 0.000001m)
            return selected;

        if (!currentSourceLocationId.HasValue)
            return selected;

        return [new(currentSourceLocationId.Value, remainderOnSource, null, null), ..selected];
    }

    internal static HashSet<long> GetSiblingCommittedSourceLocationIds(
        WarehouseTransferTask task,
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        ProductionTransferHeaderLink link)
    {
        var lineLink = link.Lines.Single(x => x.WarehouseTransferLineId == line.Id);
        var groupKey = BuildRouteSplitGroupKey(lineLink, line);

        var committed = new HashSet<long>();
        foreach (var siblingTaskLine in task.Lines.Where(x => !x.IsDeleted && x.Id != taskLine.Id))
        {
            if (siblingTaskLine.PlannedQuantity - siblingTaskLine.ProcessedQuantity <= 0) continue;
            var siblingLine = siblingTaskLine.Line;
            if (siblingLine is null || siblingLine.Trackings.Count > 0) continue;
            var siblingLink = link.Lines.SingleOrDefault(x => x.WarehouseTransferLineId == siblingLine.Id);
            if (siblingLink is null) continue;
            if (BuildRouteSplitGroupKey(siblingLink, siblingLine) != groupKey) continue;

            var locationId = siblingTaskLine.SourceLocationId ?? siblingLine.DefaultSourceLocationId;
            if (locationId.HasValue)
                committed.Add(locationId.Value);
        }

        return committed;
    }

    internal static IReadOnlyList<LocationStockBalance> ExcludeLocations(
        IReadOnlyCollection<LocationStockBalance> balances,
        IReadOnlySet<long> excludedLocationIds) =>
        balances.Where(x => !excludedLocationIds.Contains(x.LocationId)).ToArray();

    internal static IReadOnlyList<RouteAllocationChunk> BuildSerialPreviewRows(
        WarehouseTransferLine line,
        decimal remainingQuantity)
    {
        if (line.Trackings.Count == 0)
            return [new(null, remainingQuantity, null, null)];

        var rows = new List<RouteAllocationChunk>();
        foreach (var tracking in line.Trackings.OrderBy(x => x.Id))
        {
            var trackingRemaining = tracking.PlannedQuantity - tracking.PickedQuantity;
            if (trackingRemaining <= 0) continue;
            rows.Add(new(
                tracking.SourceLocationId ?? line.DefaultSourceLocationId,
                trackingRemaining,
                tracking.SerialNo,
                tracking.LotNo));
        }

        if (rows.Count == 0 && remainingQuantity > 0)
            rows.Add(new(null, remainingQuantity, null, null));
        return rows;
    }
}
