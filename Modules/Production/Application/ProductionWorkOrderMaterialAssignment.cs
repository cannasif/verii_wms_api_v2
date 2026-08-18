using System.Globalization;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.Production.Application;

public readonly record struct ProductionRecipeMaterialKey(long? StockId, long? YapCodeId, int OperationNumber);

public static class ProductionWorkOrderMaterialAssignment
{
    internal const char RequirementReferenceSeparator = '#';

    internal static string BuildRequirementReference(string workOrderNumber, int operationNumber) =>
        $"{workOrderNumber.Trim()}{RequirementReferenceSeparator}{operationNumber.ToString(CultureInfo.InvariantCulture)}";

    internal static bool TryParseOperationNumber(string? requirementReference, out int operationNumber)
    {
        operationNumber = 0;
        if (string.IsNullOrWhiteSpace(requirementReference)) return false;

        var separatorIndex = requirementReference.LastIndexOf(RequirementReferenceSeparator);
        if (separatorIndex < 0 || separatorIndex >= requirementReference.Length - 1) return false;

        return int.TryParse(
            requirementReference[(separatorIndex + 1)..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out operationNumber);
    }

    internal static ProductionRecipeMaterialKey CreateKey(long? stockId, long? yapCodeId, int operationNumber) =>
        new(stockId, yapCodeId, operationNumber);

    internal static IReadOnlyList<PreparedNetsisProductionMaterial> ApplyAssignedCoverage(
        IReadOnlyList<PreparedNetsisProductionMaterial> materials,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> assignedByKey) =>
        SplitByAssignedCoverage(materials, assignedByKey).Remaining;

    internal static IReadOnlyList<PreparedNetsisProductionMaterial> ExtractAssignedMaterials(
        IReadOnlyList<PreparedNetsisProductionMaterial> materials,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> assignedByKey) =>
        SplitByAssignedCoverage(materials, assignedByKey).Assigned;

    internal static (IReadOnlyList<PreparedNetsisProductionMaterial> Remaining, IReadOnlyList<PreparedNetsisProductionMaterial> Assigned)
        SplitByAssignedCoverage(
            IReadOnlyList<PreparedNetsisProductionMaterial> materials,
            IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> assignedByKey)
    {
        if (materials.Count == 0) return ([], []);

        var remainingAssigned = assignedByKey.ToDictionary(x => x.Key, x => x.Value);
        var remaining = new List<PreparedNetsisProductionMaterial>(materials.Count);
        var assigned = new List<PreparedNetsisProductionMaterial>(materials.Count);

        foreach (var material in materials)
        {
            var exactKey = CreateKey(material.StockId, material.YapCodeId, material.OperationNumber);
            var legacyKey = CreateKey(material.StockId, material.YapCodeId, 0);
            var assignedQuantity = ConsumeAssignedQuantity(remainingAssigned, exactKey, legacyKey, material.RequiredQuantity);
            if (assignedQuantity > QuantityTolerance)
                assigned.Add(ScaleMaterialQuantity(material, assignedQuantity));

            var remainingQuantity = material.RequiredQuantity - assignedQuantity;
            if (remainingQuantity > QuantityTolerance)
                remaining.Add(ScaleMaterialQuantity(material, remainingQuantity));
        }

        return (remaining, assigned);
    }

    internal static decimal ResolveEffectivePickedQuantity(WarehouseTransferLine line)
    {
        if (line.Trackings.Count == 0)
            return line.PickedQuantity;

        return line.Trackings
            .Where(tracking => !tracking.IsDeleted)
            .Sum(tracking => tracking.PickedQuantity);
    }

    internal static decimal ResolveCommittedAssignedQuantity(
        ProductionTransferWorkflowStatus workflowStatus,
        decimal requiredQuantity,
        decimal handedOverQuantity,
        WarehouseTransferLine transferLine)
    {
        if (workflowStatus is ProductionTransferWorkflowStatus.Completed
            or ProductionTransferWorkflowStatus.CompletedWithShortage)
        {
            if (handedOverQuantity > QuantityTolerance)
                return handedOverQuantity;

            return ResolveEffectivePickedQuantity(transferLine);
        }

        if (requiredQuantity > QuantityTolerance)
            return requiredQuantity;

        return transferLine.RequestedQuantity;
    }

    internal static decimal ResolveCommittedAssignedQuantity(
        ProductionTransferWorkflowStatus workflowStatus,
        decimal requiredQuantity,
        decimal handedOverQuantity,
        decimal requestedQuantity) =>
        workflowStatus is ProductionTransferWorkflowStatus.Completed
            or ProductionTransferWorkflowStatus.CompletedWithShortage
            ? handedOverQuantity
            : requiredQuantity > QuantityTolerance
                ? requiredQuantity
                : requestedQuantity;

    internal static decimal ResolveOpenPartialTransferRemainderQuantity(ProductionTransferLineLink linkLine)
    {
        var transferLine = linkLine.WarehouseTransferLine;
        if (transferLine is null || transferLine.IsDeleted) return 0;

        var requested = linkLine.RequiredQuantity > QuantityTolerance
            ? linkLine.RequiredQuantity
            : transferLine.RequestedQuantity;
        var picked = ResolveEffectivePickedQuantity(transferLine);
        if (linkLine.HandedOverQuantity > QuantityTolerance)
            return Math.Max(0, requested - linkLine.HandedOverQuantity);

        return Math.Max(0, requested - picked);
    }

    internal static void NetPartialTransferRemaindersAgainstOpenAssignments(
        Dictionary<ProductionRecipeMaterialKey, decimal> remainderTotals,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> openManualAssignments)
    {
        foreach (var key in remainderTotals.Keys.ToList())
        {
            var net = Math.Max(0, remainderTotals[key] - openManualAssignments.GetValueOrDefault(key));
            if (net <= QuantityTolerance) remainderTotals.Remove(key);
            else remainderTotals[key] = net;
        }
    }

    internal static Dictionary<ProductionRecipeMaterialKey, decimal> ResolveManagerCancellationQuantities(
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> requested,
        IReadOnlySet<ProductionRecipeMaterialKey> draftRevertedKeys)
    {
        if (draftRevertedKeys.Count == 0)
            return requested.ToDictionary(x => x.Key, x => x.Value);

        return requested
            .Where(x => !draftRevertedKeys.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value);
    }

    internal static bool IsFullyAssigned(
        IReadOnlyList<PreparedNetsisProductionMaterial> materials,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> assignedByKey,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal>? partialTransferRemainderByKey = null)
    {
        if (materials.Count == 0) return false;

        if (partialTransferRemainderByKey is not null
            && partialTransferRemainderByKey.Values.Any(quantity => quantity > QuantityTolerance))
            return false;

        var remainingAssigned = assignedByKey.ToDictionary(x => x.Key, x => x.Value);
        foreach (var material in materials)
        {
            var exactKey = CreateKey(material.StockId, material.YapCodeId, material.OperationNumber);
            var legacyKey = CreateKey(material.StockId, material.YapCodeId, 0);
            var assigned = ConsumeAssignedQuantity(remainingAssigned, exactKey, legacyKey, material.RequiredQuantity);
            if (material.RequiredQuantity - assigned > QuantityTolerance) return false;
        }

        return true;
    }

    internal static (IReadOnlyList<PreparedNetsisProductionMaterial> Remaining, IReadOnlyList<PreparedNetsisProductionMaterial> Assigned)
        ReclassifyPartialTransferRemainders(
            IReadOnlyList<PreparedNetsisProductionMaterial> recipeMaterials,
            IReadOnlyList<PreparedNetsisProductionMaterial> remaining,
            IReadOnlyList<PreparedNetsisProductionMaterial> assigned,
            IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> remainderByKey)
    {
        if (remainderByKey.Count == 0 || remainderByKey.Values.All(quantity => quantity <= QuantityTolerance))
            return (remaining, assigned);

        var remainingTotals = AggregateQuantitiesByKey(remaining);
        var assignedTotals = AggregateQuantitiesByKey(assigned);
        foreach (var (key, remainderQuantity) in remainderByKey)
        {
            if (remainderQuantity <= QuantityTolerance) continue;

            var currentRemaining = remainingTotals.GetValueOrDefault(key);
            if (Math.Abs(currentRemaining - remainderQuantity) <= QuantityTolerance)
                continue;

            if (currentRemaining < remainderQuantity)
            {
                var deficit = remainderQuantity - currentRemaining;
                var movedFromAssigned = Math.Min(deficit, assignedTotals.GetValueOrDefault(key));
                if (movedFromAssigned > QuantityTolerance)
                {
                    assignedTotals[key] -= movedFromAssigned;
                    if (assignedTotals[key] <= QuantityTolerance) assignedTotals.Remove(key);
                }

                remainingTotals[key] = currentRemaining + movedFromAssigned;
            }
            else
            {
                var excess = currentRemaining - remainderQuantity;
                remainingTotals[key] = remainderQuantity;
                if (excess > QuantityTolerance)
                    assignedTotals[key] = assignedTotals.GetValueOrDefault(key) + excess;
            }
        }

        return (
            BuildScaledMaterials(recipeMaterials, remainingTotals),
            BuildScaledMaterials(recipeMaterials, assignedTotals));
    }

    internal static IReadOnlyList<PreparedNetsisProductionMaterial> SubtractCancelledQuantities(
        IReadOnlyList<PreparedNetsisProductionMaterial> materials,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> cancelledByKey)
    {
        if (materials.Count == 0 || cancelledByKey.Count == 0) return materials;

        var totals = AggregateQuantitiesByKey(materials);
        foreach (var (key, cancelledQuantity) in cancelledByKey)
        {
            if (!totals.TryGetValue(key, out var current)) continue;
            var next = Math.Max(0, current - cancelledQuantity);
            if (next <= QuantityTolerance) totals.Remove(key);
            else totals[key] = next;
        }

        return BuildScaledMaterials(materials, totals);
    }

    private static Dictionary<ProductionRecipeMaterialKey, decimal> AggregateQuantitiesByKey(
        IReadOnlyList<PreparedNetsisProductionMaterial> materials)
    {
        var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
        foreach (var material in materials)
        {
            var key = CreateKey(material.StockId, material.YapCodeId, material.OperationNumber);
            totals[key] = totals.GetValueOrDefault(key) + material.RequiredQuantity;
        }

        return totals;
    }

    private static IReadOnlyList<PreparedNetsisProductionMaterial> BuildScaledMaterials(
        IReadOnlyList<PreparedNetsisProductionMaterial> recipeMaterials,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> quantitiesByKey)
    {
        if (quantitiesByKey.Count == 0) return [];

        var recipeByKey = recipeMaterials.ToDictionary(
            material => CreateKey(material.StockId, material.YapCodeId, material.OperationNumber));
        var scaled = new List<PreparedNetsisProductionMaterial>(quantitiesByKey.Count);
        foreach (var (key, quantity) in quantitiesByKey)
        {
            if (quantity <= QuantityTolerance) continue;
            if (!recipeByKey.TryGetValue(key, out var template)) continue;
            scaled.Add(ScaleMaterialQuantity(template, quantity));
        }

        return scaled
            .OrderBy(material => material.OperationNumber)
            .ThenBy(material => material.StockCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private const decimal QuantityTolerance = 0.0001m;

    private static decimal ConsumeAssignedQuantity(
        Dictionary<ProductionRecipeMaterialKey, decimal> remainingAssigned,
        ProductionRecipeMaterialKey exactKey,
        ProductionRecipeMaterialKey legacyKey,
        decimal requiredQuantity)
    {
        if (remainingAssigned.TryGetValue(exactKey, out var exactAssigned) && exactAssigned > QuantityTolerance)
        {
            var used = Math.Min(requiredQuantity, exactAssigned);
            remainingAssigned[exactKey] = exactAssigned - used;
            if (remainingAssigned[exactKey] <= QuantityTolerance) remainingAssigned.Remove(exactKey);
            return used;
        }

        if (remainingAssigned.TryGetValue(legacyKey, out var legacyAssigned) && legacyAssigned > QuantityTolerance)
        {
            var used = Math.Min(requiredQuantity, legacyAssigned);
            remainingAssigned[legacyKey] = legacyAssigned - used;
            if (remainingAssigned[legacyKey] <= QuantityTolerance) remainingAssigned.Remove(legacyKey);
            return used;
        }

        return 0;
    }

    private static PreparedNetsisProductionMaterial ScaleMaterialQuantity(
        PreparedNetsisProductionMaterial material,
        decimal remainingQuantity)
    {
        if (material.RequiredQuantity <= QuantityTolerance)
            return material with { RequiredQuantity = remainingQuantity };

        var ratio = remainingQuantity / material.RequiredQuantity;
        return material with
        {
            RequiredQuantity = remainingQuantity,
            RecipeQuantity = material.RecipeQuantity * ratio,
            WasteQuantity = material.WasteQuantity * ratio,
        };
    }

    internal static IReadOnlyList<PreparedNetsisProductionMaterial> BuildKalanOpenMaterials(
        ProductionTransferHeaderLink link,
        WarehouseTransferTask kalanTask)
    {
        var lineLinksByTransferLineId = link.Lines
            .Where(line => !line.IsDeleted)
            .ToDictionary(line => line.WarehouseTransferLineId);
        var materials = new List<PreparedNetsisProductionMaterial>();
        foreach (var taskLine in kalanTask.Lines
                     .Where(line => !line.IsDeleted)
                     .OrderBy(line => line.Id))
        {
            var openQuantity = Math.Max(0, taskLine.PlannedQuantity - taskLine.ProcessedQuantity);
            if (openQuantity <= QuantityTolerance)
                continue;

            var transferLine = taskLine.Line;
            lineLinksByTransferLineId.TryGetValue(transferLine.Id, out var lineLink);
            var operationNumber = lineLink is not null
                && TryParseOperationNumber(lineLink.RequirementReference, out var parsedOperation)
                ? parsedOperation
                : 0;

            materials.Add(new PreparedNetsisProductionMaterial(
                transferLine.StockId,
                $"STK-{transferLine.StockId}",
                null,
                transferLine.UnitCode ?? "ADET",
                transferLine.YapCodeId,
                null,
                operationNumber,
                openQuantity,
                0,
                openQuantity,
                null));
        }

        return ConsolidateSameRequirementMaterials(materials);
    }

    /// <summary>
    /// Atanmayanlar reçete/açıklama görünümü: aynı stok + aynı ihtiyaç satırlarını birleştirir.
    /// Kayıtlı transfer/görev satırlarına dokunmaz.
    /// </summary>
    internal static IReadOnlyList<PreparedNetsisProductionMaterial> ConsolidateSameRequirementMaterials(
        IReadOnlyList<PreparedNetsisProductionMaterial> materials)
    {
        if (materials.Count <= 1) return materials;

        var grouped = new List<PreparedNetsisProductionMaterial>(materials.Count);
        var indexByKey = new Dictionary<(ProductionRecipeMaterialKey Key, string UnitCode), int>();
        foreach (var material in materials)
        {
            var key = (
                CreateKey(material.StockId, material.YapCodeId, material.OperationNumber),
                NormalizeUnitCode(material.UnitCode));
            if (indexByKey.TryGetValue(key, out var index))
            {
                var current = grouped[index];
                grouped[index] = current with
                {
                    RecipeQuantity = current.RecipeQuantity + material.RecipeQuantity,
                    WasteQuantity = current.WasteQuantity + material.WasteQuantity,
                    RequiredQuantity = current.RequiredQuantity + material.RequiredQuantity,
                    MappingError = current.MappingError ?? material.MappingError,
                };
                continue;
            }

            indexByKey[key] = grouped.Count;
            grouped.Add(material);
        }

        return grouped;
    }

    private static string NormalizeUnitCode(string? unitCode) =>
        string.IsNullOrWhiteSpace(unitCode) ? "ADET" : unitCode.Trim().ToUpperInvariant();
}
