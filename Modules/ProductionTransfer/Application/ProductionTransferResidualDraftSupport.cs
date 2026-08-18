using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal sealed record ResidualDraftGroup(
    WarehouseTransferLine SourceLine,
    ProductionTransferLineLink SourceLink,
    decimal RemainingQuantity,
    WarehouseTransferLineDraftRequest Draft);

internal static class ProductionTransferResidualDraftSupport
{
    private const decimal QuantityTolerance = 0.0001m;

    internal static IReadOnlyList<ResidualDraftGroup> BuildConsolidatedResidualGroups(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink originalLink,
        WarehouseTransferTask? pickTask)
    {
        var linkByLineId = originalLink.Lines
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.WarehouseTransferLineId)
            .ToDictionary(x => x.Key, x => x.First());

        var remainingLines = header.Lines
            .Where(x => !x.IsDeleted)
            .Select(line => (Line: line, Remaining: GetRemainingQuantity(line)))
            .Where(x => x.Remaining > 0)
            .OrderBy(x => x.Line.LineNo)
            .ThenBy(x => x.Line.Id)
            .ToArray();

        var groups = remainingLines
            .GroupBy(x => BuildConsolidateKey(x.Line, linkByLineId.GetValueOrDefault(x.Line.Id)))
            .Select(group =>
            {
                var members = group.ToArray();
                var first = members[0];
                if (!linkByLineId.TryGetValue(first.Line.Id, out var sourceLink))
                    throw AppException.Conflict("Eksik teslim kalan satırının üretim bağlantısı bulunamadı.");

                var remainingQuantity = members.Sum(x => x.Remaining);
                var draft = BuildLineDraft(header, first.Line, pickTask, remainingQuantity);
                if (members.Length > 1)
                {
                    var sources = members
                        .Select(x => ResolveResidualSourceLocationId(header, x.Line, pickTask))
                        .Distinct()
                        .ToArray();
                    var targets = members
                        .Select(x => ResolveResidualTargetLocationId(header, x.Line))
                        .Distinct()
                        .ToArray();
                    draft = draft with
                    {
                        DefaultSourceLocationId = sources.Length == 1 ? sources[0] : null,
                        DefaultTargetLocationId = targets.Length == 1
                            ? targets[0]
                            : ResolveResidualTargetLocationId(header, first.Line),
                    };
                }

                return new ResidualDraftGroup(first.Line, sourceLink, remainingQuantity, draft);
            })
            .ToArray();

        return groups;
    }

    internal static decimal GetRemainingQuantity(WarehouseTransferLine line) =>
        Math.Max(0, line.RequestedQuantity - ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line));

    internal static WarehouseTransferLineDraftRequest BuildLineDraft(
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        WarehouseTransferTask? pickTask,
        decimal remainingQuantity)
    {
        var sourceLocationId = ResolveResidualSourceLocationId(header, line, pickTask);
        var targetLocationId = ResolveResidualTargetLocationId(header, line);
        var trackings = BuildResidualTrackings(line, sourceLocationId, targetLocationId);
        var draft = new WarehouseTransferLineDraftRequest(
            line.StockId,
            line.YapCodeId,
            remainingQuantity,
            line.UnitCode,
            line.TrackingType,
            line.RequireHandlingUnit,
            sourceLocationId,
            targetLocationId,
            $"{header.DocumentNo} eksik tesliminden kalan miktar",
            trackings,
            null,
            line.SourceStockStatus,
            line.TargetStockStatus);

        return RequiresDeferredTrackingCapture(draft)
            ? draft with { Trackings = null }
            : draft;
    }

    internal static bool NeedsAutoAssignSources(
        WarehouseTransferHeader header,
        IReadOnlyList<WarehouseTransferLineDraftRequest> draftLines) =>
        draftLines.Any(RequiresDeferredTrackingCapture)
        || draftLines.Any(x => !x.DefaultSourceLocationId.HasValue)
        || (header.SourceWarehouseId == header.TargetWarehouseId
            && draftLines.Any(x => !x.DefaultTargetLocationId.HasValue));

    internal static bool RequiresDeferredTrackingCapture(WarehouseTransferLineDraftRequest line)
    {
        if (line.TrackingType == StockTrackingType.None)
            return false;

        if (line.Trackings is null || line.Trackings.Count == 0)
            return true;

        if (line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial
            && line.Trackings.Any(x => string.IsNullOrWhiteSpace(x.SerialNo)))
            return true;

        if (line.TrackingType is StockTrackingType.Lot or StockTrackingType.LotAndSerial
            && line.Trackings.Any(x => string.IsNullOrWhiteSpace(x.LotNo)))
            return true;

        var capturedQuantity = line.Trackings.Sum(x => x.Quantity);
        return Math.Abs(capturedQuantity - line.Quantity) > QuantityTolerance;
    }

    internal static long? ResolveResidualSourceLocationId(
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        WarehouseTransferTask? pickTask)
    {
        var candidates = new HashSet<long>();
        if (line.DefaultSourceLocationId.HasValue)
            candidates.Add(line.DefaultSourceLocationId.Value);

        foreach (var taskLine in EnumerateOpenPickTaskLines(pickTask, line.Id))
        {
            if (taskLine.SourceLocationId.HasValue)
                candidates.Add(taskLine.SourceLocationId.Value);
        }

        foreach (var tracking in line.Trackings.Where(x => !x.IsDeleted && x.PlannedQuantity > x.PickedQuantity))
        {
            if (tracking.SourceLocationId.HasValue)
                candidates.Add(tracking.SourceLocationId.Value);
        }

        if (candidates.Count == 1)
            return candidates.First();

        var openTaskSources = EnumerateOpenPickTaskLines(pickTask, line.Id)
            .Select(x => x.SourceLocationId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        if (openTaskSources.Length == 1)
            return openTaskSources[0];

        var processedTaskSources = EnumeratePickTaskLines(pickTask, line.Id)
            .Select(x => x.SourceLocationId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        if (processedTaskSources.Length == 1)
            return processedTaskSources[0];

        return line.DefaultSourceLocationId;
    }

    internal static long? ResolveResidualTargetLocationId(
        WarehouseTransferHeader header,
        WarehouseTransferLine line)
    {
        if (line.DefaultTargetLocationId.HasValue)
            return line.DefaultTargetLocationId;

        if (header.SourceWarehouseId == header.TargetWarehouseId)
        {
            try
            {
                return ProductionTransferLocationPolicy.ResolveHandoverTargetLocationId(header, line);
            }
            catch
            {
                return header.TargetPutawayLocationId;
            }
        }

        return header.TargetPutawayLocationId;
    }

    internal static WarehouseTransferTask? ResolvePrimaryPickTask(WarehouseTransferHeader header) =>
        header.Tasks
            .Where(x => !x.IsDeleted && x.TaskType == WarehouseTransferTaskType.Pick)
            .OrderByDescending(x => x.Id)
            .FirstOrDefault();

    private static IEnumerable<WarehouseTransferTaskLine> EnumerateOpenPickTaskLines(
        WarehouseTransferTask? pickTask,
        long wtLineId) =>
        pickTask?.Lines.Where(x =>
            !x.IsDeleted
            && x.WtLineId == wtLineId
            && x.PlannedQuantity - x.ProcessedQuantity > 0)
        ?? [];

    private static IEnumerable<WarehouseTransferTaskLine> EnumeratePickTaskLines(
        WarehouseTransferTask? pickTask,
        long wtLineId) =>
        pickTask?.Lines.Where(x => !x.IsDeleted && x.WtLineId == wtLineId)
        ?? [];

    private static IReadOnlyList<WarehouseTransferTrackingDraftRequest>? BuildResidualTrackings(
        WarehouseTransferLine line,
        long? defaultSourceLocationId,
        long? defaultTargetLocationId)
    {
        if (line.Trackings.Count == 0) return null;

        var unpicked = line.Trackings
            .Where(x => !x.IsDeleted && x.PlannedQuantity > x.PickedQuantity);

        if (line.RequireSerial)
            unpicked = unpicked.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo));
        else if (line.RequireLot)
            unpicked = unpicked.Where(x => !string.IsNullOrWhiteSpace(x.LotNo));

        var trackings = unpicked
            .Select(x => new WarehouseTransferTrackingDraftRequest(
                x.PlannedQuantity - x.PickedQuantity,
                x.HandlingUnitNo,
                x.LotNo,
                x.SerialNo,
                x.ManufacturingDate,
                x.ExpirationDate,
                x.SourceLocationId ?? defaultSourceLocationId,
                x.TargetLocationId ?? defaultTargetLocationId))
            .ToArray();

        return trackings.Length == 0 ? null : trackings;
    }

    private static ResidualConsolidateKey BuildConsolidateKey(
        WarehouseTransferLine line,
        ProductionTransferLineLink? sourceLink)
    {
        if (!CanConsolidateResidualLine(line) || sourceLink is null)
            return ResidualConsolidateKey.Distinct(line.Id);

        return ResidualConsolidateKey.Shared(
            sourceLink.ProductionConsumptionId,
            sourceLink.ProductionOutputId,
            sourceLink.RequirementReference ?? string.Empty,
            sourceLink.LineRole,
            line.StockId,
            line.YapCodeId,
            line.UnitCode.Trim(),
            line.RequireHandlingUnit,
            line.SourceStockStatus,
            line.TargetStockStatus);
    }

    private static bool CanConsolidateResidualLine(WarehouseTransferLine line) =>
        line.TrackingType == StockTrackingType.None
        && !line.RequireSerial
        && !line.RequireLot
        && line.Trackings.All(x => x.IsDeleted);

    private readonly record struct ResidualConsolidateKey(
        bool Consolidatable,
        long DistinctLineId,
        long? ProductionConsumptionId,
        long? ProductionOutputId,
        string RequirementReference,
        ProductionTransferLineRole LineRole,
        long StockId,
        long? YapCodeId,
        string UnitCode,
        bool RequireHandlingUnit,
        string? SourceStockStatus,
        string? TargetStockStatus)
    {
        internal static ResidualConsolidateKey Distinct(long lineId) =>
            new(false, lineId, null, null, string.Empty, default, 0, null, string.Empty, false, null, null);

        internal static ResidualConsolidateKey Shared(
            long? productionConsumptionId,
            long? productionOutputId,
            string requirementReference,
            ProductionTransferLineRole lineRole,
            long stockId,
            long? yapCodeId,
            string unitCode,
            bool requireHandlingUnit,
            string? sourceStockStatus,
            string? targetStockStatus) =>
            new(
                true,
                0,
                productionConsumptionId,
                productionOutputId,
                requirementReference,
                lineRole,
                stockId,
                yapCodeId,
                unitCode,
                requireHandlingUnit,
                sourceStockStatus,
                targetStockStatus);
    }
}

