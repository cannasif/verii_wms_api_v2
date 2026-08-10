using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.Production.Application;

public sealed partial class ProductionService
{
    private sealed class WorkOrderAssignmentSnapshot
    {
        private readonly HashSet<string> _withTransfers;
        private readonly IReadOnlyDictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> _assignedByWorkOrder;
        private readonly IReadOnlyDictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> _partialByWorkOrder;
        private readonly IReadOnlyDictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> _cancelledByWorkOrder;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<PreparedNetsisProductionMaterial>> _recipesByWorkOrder;
        private readonly IReadOnlyDictionary<string, int> _assignedLineCounts;

        public WorkOrderAssignmentSnapshot(
            HashSet<string> withTransfers,
            IReadOnlyDictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> assignedByWorkOrder,
            IReadOnlyDictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> partialByWorkOrder,
            IReadOnlyDictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> cancelledByWorkOrder,
            IReadOnlyDictionary<string, IReadOnlyList<PreparedNetsisProductionMaterial>> recipesByWorkOrder,
            IReadOnlyDictionary<string, int> assignedLineCounts)
        {
            _withTransfers = withTransfers;
            _assignedByWorkOrder = assignedByWorkOrder;
            _partialByWorkOrder = partialByWorkOrder;
            _cancelledByWorkOrder = cancelledByWorkOrder;
            _recipesByWorkOrder = recipesByWorkOrder;
            _assignedLineCounts = assignedLineCounts;
        }

        public HashSet<string> GetFullyAssignedWorkOrderNumbers(IEnumerable<ProductionSourceWorkOrderRow> candidates)
        {
            var fullyAssigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in candidates)
            {
                var workOrderNumber = row.WorkOrderNumber.Trim();
                if (!_withTransfers.Contains(workOrderNumber))
                    continue;

                var recipeMaterials = _recipesByWorkOrder.GetValueOrDefault(workOrderNumber) ?? [];
                if (recipeMaterials.Count == 0)
                    continue;

                var assignedMaterials = _assignedByWorkOrder.GetValueOrDefault(workOrderNumber) ?? [];
                var partialTransferRemainders = _partialByWorkOrder.GetValueOrDefault(workOrderNumber) ?? [];
                if (ProductionWorkOrderMaterialAssignment.IsFullyAssigned(recipeMaterials, assignedMaterials, partialTransferRemainders)
                    || IsRemainingFullyCancelled(row))
                    fullyAssigned.Add(workOrderNumber);
            }

            return fullyAssigned;
        }

        public bool IsRemainingFullyCancelled(ProductionSourceWorkOrderRow row)
        {
            var workOrderNumber = row.WorkOrderNumber.Trim();
            var cancelledMaterials = _cancelledByWorkOrder.GetValueOrDefault(workOrderNumber);
            if (cancelledMaterials is null || cancelledMaterials.Count == 0)
                return false;

            return BuildCancellableRemainingQuantities(row).Count == 0;
        }

        public int GetAssignedRecipeLineCount(string workOrderNumber) =>
            _assignedLineCounts.GetValueOrDefault(workOrderNumber.Trim());

        public int GetRecipeLineCount(string workOrderNumber) =>
            _recipesByWorkOrder.GetValueOrDefault(workOrderNumber.Trim())?.Count ?? 0;

        private Dictionary<ProductionRecipeMaterialKey, decimal> BuildCancellableRemainingQuantities(
            ProductionSourceWorkOrderRow templateRow)
        {
            var workOrderNumber = templateRow.WorkOrderNumber.Trim();
            var recipeMaterials = _recipesByWorkOrder.GetValueOrDefault(workOrderNumber) ?? [];
            if (recipeMaterials.Count == 0)
                return [];

            var assignedMaterials = _assignedByWorkOrder.GetValueOrDefault(workOrderNumber) ?? [];
            var partialTransferRemainders = _partialByWorkOrder.GetValueOrDefault(workOrderNumber) ?? [];
            var cancelledMaterials = _cancelledByWorkOrder.GetValueOrDefault(workOrderNumber) ?? [];

            var splitMaterials = ProductionWorkOrderMaterialAssignment.SplitByAssignedCoverage(recipeMaterials, assignedMaterials);
            var reclassified = ProductionWorkOrderMaterialAssignment.ReclassifyPartialTransferRemainders(
                recipeMaterials,
                splitMaterials.Remaining,
                splitMaterials.Assigned,
                partialTransferRemainders);

            var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
            foreach (var material in reclassified.Remaining)
            {
                var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                    material.StockId,
                    material.YapCodeId,
                    material.OperationNumber);
                totals[key] = totals.GetValueOrDefault(key) + material.RequiredQuantity;
            }

            foreach (var (key, cancelledQuantity) in cancelledMaterials)
                totals[key] = Math.Max(0, totals.GetValueOrDefault(key) - cancelledQuantity);

            return totals
                .Where(x => x.Value > 0.0001m)
                .ToDictionary(x => x.Key, x => x.Value);
        }
    }

    private async Task<WorkOrderAssignmentSnapshot> BuildWorkOrderAssignmentSnapshotAsync(
        string branch,
        IReadOnlyList<ProductionSourceWorkOrderRow> rows,
        CancellationToken ct)
    {
        var normalizedWorkOrders = rows
            .Select(x => x.WorkOrderNumber.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedWorkOrders.Length == 0)
        {
            return new WorkOrderAssignmentSnapshot(
                [],
                new Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<PreparedNetsisProductionMaterial>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        }

        var normalizedSet = new HashSet<string>(normalizedWorkOrders, StringComparer.OrdinalIgnoreCase);
        var links = await LoadProductionTransferLinksForWorkOrdersAsync(branch, normalizedWorkOrders, ct);
        var cancelledByWorkOrder = await LoadCancelledMaterialQuantitiesByWorkOrderAsync(branch, normalizedWorkOrders, ct);
        var assignedByWorkOrder = AggregateAssignedMaterialQuantitiesByWorkOrder(links, normalizedSet);
        var openManualByWorkOrder = AggregateOpenManualAssignmentQuantitiesByWorkOrder(links, normalizedSet);
        var partialByWorkOrder = AggregatePartialTransferRemainderQuantitiesByWorkOrder(
            links,
            normalizedSet,
            openManualByWorkOrder);
        var assignedLineCounts = AggregateAssignedRecipeLineCountsByWorkOrder(links, normalizedSet);

        var withTransfers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var workOrderNumber = ResolveLinkedWorkOrderNumber(link, normalizedSet);
            if (workOrderNumber is not null)
                withTransfers.Add(workOrderNumber);
        }

        var recipeWorkOrders = withTransfers
            .Union(cancelledByWorkOrder.Where(x => x.Value.Count > 0).Select(x => x.Key))
            .Union(assignedLineCounts.Where(x => x.Value > 0).Select(x => x.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recipeRows = rows
            .Where(row => recipeWorkOrders.Contains(row.WorkOrderNumber.Trim()))
            .GroupBy(row => row.WorkOrderNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var recipesByWorkOrder = await LoadRecipeMaterialsByWorkOrderAsync(branch, recipeRows, ct);

        return new WorkOrderAssignmentSnapshot(
            withTransfers,
            assignedByWorkOrder,
            partialByWorkOrder,
            cancelledByWorkOrder,
            recipesByWorkOrder,
            assignedLineCounts);
    }

    private async Task<IReadOnlyList<ProductionTransferHeaderLink>> LoadProductionTransferLinksForWorkOrdersAsync(
        string branch,
        IReadOnlyCollection<string> workOrderNumbers,
        CancellationToken ct)
    {
        var normalized = workOrderNumbers
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            return [];

        var contexts = ProductionSourceWorkOrderAssignmentFilter.ProductionContexts;
        return await uow.Repository<ProductionTransferHeaderLink>().Query()
            .AsNoTracking()
            .Where(x => x.BranchCode == branch
                && contexts.Contains(x.WarehouseTransferHeader.BusinessContext)
                && x.WarehouseTransferHeader.Status != WarehouseTransferStatus.Cancelled
                && x.WorkflowStatus != ProductionTransferWorkflowStatus.Cancelled
                && ((x.ProductionOrderNo != null && normalized.Contains(x.ProductionOrderNo))
                    || (x.WarehouseTransferHeader.ExternalReferenceNo != null
                        && normalized.Contains(x.WarehouseTransferHeader.ExternalReferenceNo))))
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
                .ThenInclude(line => line.WarehouseTransferLine)
                    .ThenInclude(line => line!.Trackings.Where(tracking => !tracking.IsDeleted))
            .Include(x => x.WarehouseTransferHeader)
                .ThenInclude(header => header.Tasks.Where(task => !task.IsDeleted))
                    .ThenInclude(task => task.Assignments)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyDictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>>> LoadCancelledMaterialQuantitiesByWorkOrderAsync(
        string branch,
        IReadOnlyCollection<string> workOrderNumbers,
        CancellationToken ct)
    {
        var normalized = workOrderNumbers
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
            return new Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>>(StringComparer.OrdinalIgnoreCase);

        var cancellations = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query()
            .AsNoTracking()
            .Where(x => x.BranchCode == branch
                && normalized.Contains(x.WorkOrderNumber)
                && x.Status == ProductionWorkOrderAssignmentCancellationStatus.Active
                && !x.IsDeleted)
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .ToListAsync(ct);

        return cancellations
            .GroupBy(x => x.WorkOrderNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => AggregateCancellationLines(group.SelectMany(cancellation => cancellation.Lines.Where(line => !line.IsDeleted))),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<PreparedNetsisProductionMaterial>>> LoadRecipeMaterialsByWorkOrderAsync(
        string branch,
        IReadOnlyList<ProductionSourceWorkOrderRow> rows,
        CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<PreparedNetsisProductionMaterial>>(StringComparer.OrdinalIgnoreCase);
        if (rows.Count == 0)
            return result;

        var wmsRows = rows
            .Where(row => row.SourceType == ProductionOrderSourceType.WmsIntegrationTables)
            .ToArray();
        if (wmsRows.Length > 0)
        {
            foreach (var (workOrderNumber, materials) in await LoadWmsRecipeMaterialsByWorkOrderAsync(branch, wmsRows, ct))
                result[workOrderNumber] = materials;
        }

        var netsisRows = rows
            .Where(row => row.SourceType != ProductionOrderSourceType.WmsIntegrationTables)
            .GroupBy(row => row.WorkOrderNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (netsisRows.Length > 0)
        {
            foreach (var row in netsisRows)
            {
                var materials = await LoadNetsisRecipeMaterialsAsync(row.WorkOrderNumber, branch, ct);
                result[row.WorkOrderNumber.Trim()] = materials;
            }
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<PreparedNetsisProductionMaterial>>> LoadWmsRecipeMaterialsByWorkOrderAsync(
        string branch,
        IReadOnlyList<ProductionSourceWorkOrderRow> rows,
        CancellationToken ct)
    {
        var workOrderNumbers = rows
            .Select(row => row.WorkOrderNumber.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceSystemCodes = rows
            .Select(row => row.SourceSystemCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sources = await uow.Repository<ProductionSourceWorkOrder>().Query()
            .AsNoTracking()
            .Include(x => x.RecipeLines)
            .Where(x => x.BranchCode == branch
                && sourceSystemCodes.Contains(x.SourceSystemCode)
                && workOrderNumbers.Contains(x.WorkOrderNumber)
                && (x.Status == ProductionSourceOrderStatus.Ready || x.Status == ProductionSourceOrderStatus.Released))
            .ToListAsync(ct);

        var latestSources = sources
            .GroupBy(x => x.WorkOrderNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(source => source.RevisionNumber)
                .ThenByDescending(source => source.SourceUpdatedAtUtc)
                .First())
            .Where(source => source.RecipeLines.Count > 0)
            .ToArray();
        if (latestSources.Length == 0)
            return new Dictionary<string, IReadOnlyList<PreparedNetsisProductionMaterial>>(StringComparer.OrdinalIgnoreCase);

        var stockCodes = latestSources
            .SelectMany(source => source.RecipeLines.Select(line => line.ComponentStockCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var stocks = await uow.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branch && stockCodes.Contains(x.ErpStockCode))
            .ToListAsync(ct);
        var stockMap = stocks
            .GroupBy(x => x.ErpStockCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var configurationCodes = latestSources
            .SelectMany(source => source.RecipeLines.Select(line => line.ComponentConfigurationCode))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var yapCodes = configurationCodes.Length == 0
            ? []
            : await uow.Repository<YapCodeEntity>().Query()
                .Where(x => x.BranchCode == branch && configurationCodes.Contains(x.ConfigurationCode))
                .ToListAsync(ct);

        long? ResolveYap(string? code, long? stockId) => string.IsNullOrWhiteSpace(code)
            ? null
            : yapCodes
                .Where(x => string.Equals(x.ConfigurationCode, code.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.StockId == stockId)
                .ThenBy(x => x.Id)
                .Select(x => (long?)x.Id)
                .FirstOrDefault();

        return latestSources.ToDictionary(
            source => source.WorkOrderNumber.Trim(),
            source => (IReadOnlyList<PreparedNetsisProductionMaterial>)source.RecipeLines
                .OrderBy(line => line.LineNumber)
                .Select(line =>
                {
                    stockMap.TryGetValue(line.ComponentStockCode, out var stock);
                    return new PreparedNetsisProductionMaterial(
                        stock?.Id,
                        line.ComponentStockCode,
                        line.ComponentStockName,
                        stock?.BaseUnitCode ?? line.UnitCode,
                        ResolveYap(line.ComponentConfigurationCode, stock?.Id),
                        line.ComponentConfigurationCode,
                        line.OperationNumber,
                        line.RecipeQuantity,
                        line.VariableWasteQuantity + line.FixedWasteQuantity,
                        line.TotalRequiredQuantity,
                        stock is null ? $"Bileşen stok WMS ERP aynasında bulunamadı: {line.ComponentStockCode}" : null);
                })
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> AggregateAssignedMaterialQuantitiesByWorkOrder(
        IReadOnlyList<ProductionTransferHeaderLink> links,
        IReadOnlySet<string> candidateWorkOrderNumbers)
    {
        var totalsByWorkOrder = new Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            if (ProductionWorkOrderTransferGrouping.IsOpenPartialTransferRemainderLink(link))
                continue;

            var workOrderNumber = ResolveLinkedWorkOrderNumber(link, candidateWorkOrderNumbers);
            if (workOrderNumber is null)
                continue;

            if (!totalsByWorkOrder.TryGetValue(workOrderNumber, out var totals))
            {
                totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
                totalsByWorkOrder[workOrderNumber] = totals;
            }

            foreach (var linkLine in link.Lines.Where(line => !line.IsDeleted))
            {
                var transferLine = linkLine.WarehouseTransferLine;
                if (transferLine is null || transferLine.IsDeleted)
                    continue;

                var quantity = ProductionWorkOrderMaterialAssignment.ResolveCommittedAssignedQuantity(
                    link.WorkflowStatus,
                    linkLine.RequiredQuantity,
                    linkLine.HandedOverQuantity,
                    transferLine);
                if (quantity <= 0)
                    continue;

                var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                    linkLine.RequirementReference,
                    out var parsedOperation)
                    ? parsedOperation
                    : 0;
                var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                    transferLine.StockId,
                    transferLine.YapCodeId,
                    operationNumber);
                totals[key] = totals.GetValueOrDefault(key) + quantity;
            }
        }

        return totalsByWorkOrder;
    }

    private static Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> AggregateOpenManualAssignmentQuantitiesByWorkOrder(
        IReadOnlyList<ProductionTransferHeaderLink> links,
        IReadOnlySet<string> candidateWorkOrderNumbers)
    {
        var totalsByWorkOrder = new Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            if (ProductionWorkOrderTransferGrouping.IsOpenPartialTransferRemainderLink(link))
                continue;
            if (link.WorkflowStatus is ProductionTransferWorkflowStatus.Completed or ProductionTransferWorkflowStatus.CompletedWithShortage)
                continue;

            var workOrderNumber = ResolveLinkedWorkOrderNumber(link, candidateWorkOrderNumbers);
            if (workOrderNumber is null)
                continue;

            if (!totalsByWorkOrder.TryGetValue(workOrderNumber, out var totals))
            {
                totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
                totalsByWorkOrder[workOrderNumber] = totals;
            }

            foreach (var linkLine in link.Lines.Where(line => !line.IsDeleted))
            {
                var transferLine = linkLine.WarehouseTransferLine;
                if (transferLine is null || transferLine.IsDeleted)
                    continue;

                var quantity = linkLine.RequiredQuantity > 0
                    ? linkLine.RequiredQuantity
                    : transferLine.RequestedQuantity;
                if (quantity <= 0)
                    continue;

                var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                    linkLine.RequirementReference,
                    out var parsedOperation)
                    ? parsedOperation
                    : 0;
                var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                    transferLine.StockId,
                    transferLine.YapCodeId,
                    operationNumber);
                totals[key] = totals.GetValueOrDefault(key) + quantity;
            }
        }

        return totalsByWorkOrder;
    }

    private static Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> AggregatePartialTransferRemainderQuantitiesByWorkOrder(
        IReadOnlyList<ProductionTransferHeaderLink> links,
        IReadOnlySet<string> candidateWorkOrderNumbers,
        IReadOnlyDictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>> openManualByWorkOrder)
    {
        var totalsByWorkOrder = new Dictionary<string, Dictionary<ProductionRecipeMaterialKey, decimal>>(StringComparer.OrdinalIgnoreCase);
        var linksByWorkOrder = links
            .Select(link => (Link: link, WorkOrderNumber: ResolveLinkedWorkOrderNumber(link, candidateWorkOrderNumbers)))
            .Where(item => item.WorkOrderNumber is not null)
            .GroupBy(item => item.WorkOrderNumber!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in linksByWorkOrder)
        {
            var workOrderLinks = group.Select(item => item.Link).ToArray();
            var activeRemainderLinks = ProductionWorkOrderTransferGrouping.FilterActiveOpenPartialTransferRemainderLinks(workOrderLinks);
            if (activeRemainderLinks.Count == 0)
                continue;

            var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
            foreach (var link in activeRemainderLinks)
            {
                foreach (var linkLine in link.Lines.Where(line => !line.IsDeleted))
                {
                    var transferLine = linkLine.WarehouseTransferLine;
                    if (transferLine is null || transferLine.IsDeleted)
                        continue;

                    var remaining = ProductionWorkOrderMaterialAssignment.ResolveOpenPartialTransferRemainderQuantity(linkLine);
                    if (remaining <= 0)
                        continue;

                    var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                        linkLine.RequirementReference,
                        out var parsedOperation)
                        ? parsedOperation
                        : 0;
                    var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                        transferLine.StockId,
                        transferLine.YapCodeId,
                        operationNumber);
                    totals[key] = totals.GetValueOrDefault(key) + remaining;
                }
            }

            var openManualAssignments = openManualByWorkOrder.GetValueOrDefault(group.Key) ?? [];
            ProductionWorkOrderMaterialAssignment.NetPartialTransferRemaindersAgainstOpenAssignments(
                totals,
                openManualAssignments);
            if (totals.Count > 0)
                totalsByWorkOrder[group.Key] = totals;
        }

        return totalsByWorkOrder;
    }

    private static Dictionary<string, int> AggregateAssignedRecipeLineCountsByWorkOrder(
        IReadOnlyList<ProductionTransferHeaderLink> links,
        IReadOnlySet<string> candidateWorkOrderNumbers)
    {
        var keysByWorkOrder = new Dictionary<string, HashSet<ProductionRecipeMaterialKey>>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            if (ProductionWorkOrderTransferGrouping.IsOpenPartialTransferRemainderLink(link))
                continue;

            var workOrderNumber = ResolveLinkedWorkOrderNumber(link, candidateWorkOrderNumbers);
            if (workOrderNumber is null)
                continue;

            if (!keysByWorkOrder.TryGetValue(workOrderNumber, out var keys))
            {
                keys = new HashSet<ProductionRecipeMaterialKey>();
                keysByWorkOrder[workOrderNumber] = keys;
            }

            foreach (var linkLine in link.Lines.Where(line => !line.IsDeleted))
            {
                var transferLine = linkLine.WarehouseTransferLine;
                if (transferLine is null || transferLine.IsDeleted)
                    continue;

                var quantity = ProductionWorkOrderMaterialAssignment.ResolveCommittedAssignedQuantity(
                    link.WorkflowStatus,
                    linkLine.RequiredQuantity,
                    linkLine.HandedOverQuantity,
                    transferLine);
                if (quantity <= 0)
                    continue;

                var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                    linkLine.RequirementReference,
                    out var parsedOperation)
                    ? parsedOperation
                    : 0;
                keys.Add(ProductionWorkOrderMaterialAssignment.CreateKey(
                    transferLine.StockId,
                    transferLine.YapCodeId,
                    operationNumber));
            }
        }

        return keysByWorkOrder.ToDictionary(
            item => item.Key,
            item => item.Value.Count,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCancellationReturnRemainderFullyAssigned(
        ProductionTransferHeaderLink sourceLink,
        WarehouseTransferTask kalanTask,
        IReadOnlyList<ProductionTransferHeaderLink> assignmentLinks,
        IReadOnlySet<string> candidateWorkOrderNumbers)
    {
        var workOrderNumber = ResolveLinkedWorkOrderNumber(sourceLink, candidateWorkOrderNumbers);
        if (workOrderNumber is null)
            return false;

        var kalanMaterials = ProductionWorkOrderMaterialAssignment.BuildKalanOpenMaterials(sourceLink, kalanTask);
        if (kalanMaterials.Count == 0)
            return true;

        var excludeHeaderId = sourceLink.WarehouseTransferHeaderId;
        var assignedMaterials = AggregateAssignedMaterialQuantitiesExcludingHeader(
            assignmentLinks,
            workOrderNumber,
            candidateWorkOrderNumbers,
            excludeHeaderId);
        var openManualAssignments = AggregateOpenManualAssignmentQuantitiesExcludingHeader(
            assignmentLinks,
            workOrderNumber,
            candidateWorkOrderNumbers,
            excludeHeaderId);
        var partialTransferRemainders = AggregatePartialTransferRemainderQuantitiesExcludingHeader(
            assignmentLinks,
            workOrderNumber,
            candidateWorkOrderNumbers,
            excludeHeaderId,
            openManualAssignments);

        return ProductionWorkOrderMaterialAssignment.IsFullyAssigned(
            kalanMaterials,
            assignedMaterials,
            partialTransferRemainders);
    }

    private static Dictionary<ProductionRecipeMaterialKey, decimal> AggregateAssignedMaterialQuantitiesExcludingHeader(
        IReadOnlyList<ProductionTransferHeaderLink> links,
        string workOrderNumber,
        IReadOnlySet<string> candidateWorkOrderNumbers,
        long excludeHeaderId)
    {
        var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
        foreach (var link in links)
        {
            if (link.WarehouseTransferHeaderId == excludeHeaderId)
                continue;
            if (ProductionWorkOrderTransferGrouping.IsOpenPartialTransferRemainderLink(link))
                continue;
            if (!string.Equals(
                    ResolveLinkedWorkOrderNumber(link, candidateWorkOrderNumbers),
                    workOrderNumber,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var linkLine in link.Lines.Where(line => !line.IsDeleted))
            {
                var transferLine = linkLine.WarehouseTransferLine;
                if (transferLine is null || transferLine.IsDeleted)
                    continue;

                var quantity = ProductionWorkOrderMaterialAssignment.ResolveCommittedAssignedQuantity(
                    link.WorkflowStatus,
                    linkLine.RequiredQuantity,
                    linkLine.HandedOverQuantity,
                    transferLine);
                if (quantity <= 0)
                    continue;

                var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                    linkLine.RequirementReference,
                    out var parsedOperation)
                    ? parsedOperation
                    : 0;
                var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                    transferLine.StockId,
                    transferLine.YapCodeId,
                    operationNumber);
                totals[key] = totals.GetValueOrDefault(key) + quantity;
            }
        }

        return totals;
    }

    private static Dictionary<ProductionRecipeMaterialKey, decimal> AggregateOpenManualAssignmentQuantitiesExcludingHeader(
        IReadOnlyList<ProductionTransferHeaderLink> links,
        string workOrderNumber,
        IReadOnlySet<string> candidateWorkOrderNumbers,
        long excludeHeaderId)
    {
        var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
        foreach (var link in links)
        {
            if (link.WarehouseTransferHeaderId == excludeHeaderId)
                continue;
            if (ProductionWorkOrderTransferGrouping.IsOpenPartialTransferRemainderLink(link))
                continue;
            if (link.WorkflowStatus is ProductionTransferWorkflowStatus.Completed or ProductionTransferWorkflowStatus.CompletedWithShortage)
                continue;
            if (!string.Equals(
                    ResolveLinkedWorkOrderNumber(link, candidateWorkOrderNumbers),
                    workOrderNumber,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var linkLine in link.Lines.Where(line => !line.IsDeleted))
            {
                var transferLine = linkLine.WarehouseTransferLine;
                if (transferLine is null || transferLine.IsDeleted)
                    continue;

                var quantity = linkLine.RequiredQuantity > 0
                    ? linkLine.RequiredQuantity
                    : transferLine.RequestedQuantity;
                if (quantity <= 0)
                    continue;

                var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                    linkLine.RequirementReference,
                    out var parsedOperation)
                    ? parsedOperation
                    : 0;
                var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                    transferLine.StockId,
                    transferLine.YapCodeId,
                    operationNumber);
                totals[key] = totals.GetValueOrDefault(key) + quantity;
            }
        }

        return totals;
    }

    private static Dictionary<ProductionRecipeMaterialKey, decimal> AggregatePartialTransferRemainderQuantitiesExcludingHeader(
        IReadOnlyList<ProductionTransferHeaderLink> links,
        string workOrderNumber,
        IReadOnlySet<string> candidateWorkOrderNumbers,
        long excludeHeaderId,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> openManualAssignments)
    {
        var workOrderLinks = links
            .Where(link => link.WarehouseTransferHeaderId != excludeHeaderId)
            .Where(link => string.Equals(
                ResolveLinkedWorkOrderNumber(link, candidateWorkOrderNumbers),
                workOrderNumber,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var activeRemainderLinks = ProductionWorkOrderTransferGrouping.FilterActiveOpenPartialTransferRemainderLinks(workOrderLinks);
        if (activeRemainderLinks.Count == 0)
            return [];

        var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
        foreach (var link in activeRemainderLinks)
        {
            foreach (var linkLine in link.Lines.Where(line => !line.IsDeleted))
            {
                var transferLine = linkLine.WarehouseTransferLine;
                if (transferLine is null || transferLine.IsDeleted)
                    continue;

                var remaining = ProductionWorkOrderMaterialAssignment.ResolveOpenPartialTransferRemainderQuantity(linkLine);
                if (remaining <= 0)
                    continue;

                var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                    linkLine.RequirementReference,
                    out var parsedOperation)
                    ? parsedOperation
                    : 0;
                var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                    transferLine.StockId,
                    transferLine.YapCodeId,
                    operationNumber);
                totals[key] = totals.GetValueOrDefault(key) + remaining;
            }
        }

        ProductionWorkOrderMaterialAssignment.NetPartialTransferRemaindersAgainstOpenAssignments(
            totals,
            openManualAssignments);

        return totals;
    }
}
