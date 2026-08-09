using System.Globalization;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public enum ProductionWorkOrderTransferTab
{
    Picking = 1,
    Completed = 2,
    Cancelled = 3,
    MyAssignments = 4
}

public static class ProductionWorkOrderTransferGrouping
{
    public sealed class LabelContext
    {
        public Dictionary<long, int> PartialTransferIndex { get; init; } = [];
        public HashSet<long> CurrentKalanHeaderIds { get; init; } = [];
    }

    public static string GetOrderKey(ProductionTransferHeaderLink link) =>
        link.ProductionOrderId?.ToString(CultureInfo.InvariantCulture)
        ?? link.ProductionHeaderId?.ToString(CultureInfo.InvariantCulture)
        ?? link.ProductionOrderNo?.Trim()
        ?? $"transfer:{link.WarehouseTransferHeaderId}";

    public static bool IsCompletedTab(ProductionTransferHeaderLink link) =>
        link.WorkflowStatus is ProductionTransferWorkflowStatus.Completed
            or ProductionTransferWorkflowStatus.CompletedWithShortage;

    public static bool HasOpenCancellationReturn(WarehouseTransferHeader header) =>
        header.Tasks.Any(task => !task.IsDeleted
            && task.TaskType == WarehouseTransferTaskType.CancellationReturn
            && task.Status is not WarehouseTransferTaskStatus.Completed
                and not WarehouseTransferTaskStatus.Cancelled);

    public static bool MatchesTab(
        ProductionWorkOrderTransferTab tab,
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link)
    {
        var openCancellationReturn = HasOpenCancellationReturn(header);
        return tab switch
        {
            ProductionWorkOrderTransferTab.Completed => IsCompletedTab(link),
            ProductionWorkOrderTransferTab.Cancelled =>
                header.Status == WarehouseTransferStatus.Cancelled && !openCancellationReturn,
            ProductionWorkOrderTransferTab.Picking =>
                !IsCompletedTab(link)
                && !(header.Status == WarehouseTransferStatus.Cancelled && !openCancellationReturn),
            _ => false
        };
    }

    public static IQueryable<ProductionTransferHeaderLink> ApplyTabFilter(
        IQueryable<ProductionTransferHeaderLink> query,
        ProductionWorkOrderTransferTab tab,
        long? currentUserId = null) =>
        tab switch
        {
            ProductionWorkOrderTransferTab.Completed => query.Where(link =>
                link.WorkflowStatus == ProductionTransferWorkflowStatus.Completed
                || link.WorkflowStatus == ProductionTransferWorkflowStatus.CompletedWithShortage),
            ProductionWorkOrderTransferTab.Cancelled => query.Where(link =>
                link.WarehouseTransferHeader.Status == WarehouseTransferStatus.Cancelled
                && !link.WarehouseTransferHeader.Tasks.Any(task => !task.IsDeleted
                    && task.TaskType == WarehouseTransferTaskType.CancellationReturn
                    && task.Status != WarehouseTransferTaskStatus.Completed
                    && task.Status != WarehouseTransferTaskStatus.Cancelled)),
            ProductionWorkOrderTransferTab.Picking => query.Where(link =>
                link.WorkflowStatus != ProductionTransferWorkflowStatus.Completed
                && link.WorkflowStatus != ProductionTransferWorkflowStatus.CompletedWithShortage
                && (link.WarehouseTransferHeader.Status != WarehouseTransferStatus.Cancelled
                    || link.WarehouseTransferHeader.Tasks.Any(task => !task.IsDeleted
                        && task.TaskType == WarehouseTransferTaskType.CancellationReturn
                        && task.Status != WarehouseTransferTaskStatus.Completed
                        && task.Status != WarehouseTransferTaskStatus.Cancelled))),
            ProductionWorkOrderTransferTab.MyAssignments => ApplyTabFilter(query, ProductionWorkOrderTransferTab.Picking)
                .Where(link => link.WarehouseTransferHeader.Tasks.Any(task => !task.IsDeleted
                    && task.Assignments.Any(a => !a.IsDeleted && a.UserId == currentUserId))),
            _ => query
        };

    public static bool MatchesSearch(string? search, WarehouseTransferHeader header, ProductionTransferHeaderLink link)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var term = search.Trim();
        return header.DocumentNo.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (header.ExternalReferenceNo?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (link.ProductionOrderNo?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public static LabelContext BuildLabelContext(IEnumerable<ProductionTransferHeaderLink> links)
    {
        var partialIndexes = new Dictionary<long, int>();
        var kalanIds = new HashSet<long>();

        foreach (var group in links.GroupBy(GetOrderKey))
        {
            var groupLinks = group.ToList();
            var byHeaderId = groupLinks.ToDictionary(x => x.WarehouseTransferHeaderId);
            var roots = groupLinks.Where(link =>
                    !link.ParentWarehouseTransferHeaderId.HasValue
                    || !byHeaderId.ContainsKey(link.ParentWarehouseTransferHeaderId.Value))
                .ToArray();

            foreach (var root in roots)
            {
                var chain = BuildResidualChain(root, byHeaderId);
                if (chain.Count == 0) continue;

                var partialIndex = 0;
                for (var index = 0; index < chain.Count - 1; index++)
                {
                    var item = chain[index];
                    if (item.WorkflowStatus == ProductionTransferWorkflowStatus.CompletedWithShortage
                        && item.ResidualWarehouseTransferHeaderId.HasValue)
                    {
                        partialIndex++;
                        partialIndexes[item.WarehouseTransferHeaderId] = partialIndex;
                    }
                }

                if (chain.Count > 1 || chain[^1].ParentWarehouseTransferHeaderId.HasValue)
                    kalanIds.Add(chain[^1].WarehouseTransferHeaderId);
            }
        }

        return new LabelContext
        {
            PartialTransferIndex = partialIndexes,
            CurrentKalanHeaderIds = kalanIds
        };
    }

    public static string? GetDisplaySuffix(
        WarehouseTransferTask task,
        ProductionTransferHeaderLink link,
        LabelContext context,
        IReadOnlyList<WarehouseTransferTask> allTasks)
    {
        if (task.TaskType is WarehouseTransferTaskType.CancellationReturn
            or WarehouseTransferTaskType.AssignmentReturn)
            return null;

        if (context.PartialTransferIndex.TryGetValue(link.WarehouseTransferHeaderId, out var partialIndex))
            return partialIndex == 1 ? "-KISMITRANSFER" : $"-KISMITRANSFER-{partialIndex}";

        if (context.CurrentKalanHeaderIds.Contains(link.WarehouseTransferHeaderId)
            && task.TaskType == WarehouseTransferTaskType.Pick)
            return "-KALANTRANSFER";

        if (IsPostAssignmentReturnUnassigned(task, allTasks))
            return "-KALANTRANSFER";

        return null;
    }

    public static string BuildDisplayLabel(string taskNo, string documentNo, string? displaySuffix)
    {
        if (string.IsNullOrEmpty(displaySuffix)) return taskNo;
        if (taskNo.Contains("-IADE", StringComparison.OrdinalIgnoreCase)) return taskNo;

        var prefix = documentNo + "-";
        if (taskNo.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(taskNo[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return documentNo + displaySuffix;

        return taskNo + displaySuffix;
    }

    private static bool IsPostAssignmentReturnUnassigned(
        WarehouseTransferTask task,
        IReadOnlyList<WarehouseTransferTask> allTasks) =>
        task.TaskType == WarehouseTransferTaskType.Pick
        && task.Status is not WarehouseTransferTaskStatus.Completed
            and not WarehouseTransferTaskStatus.Cancelled
        && !task.Assignments.Any(assignment => !assignment.IsDeleted)
        && task.PreviousTaskId is long originTaskId
        && allTasks.Any(other => !other.IsDeleted
            && other.TaskType == WarehouseTransferTaskType.AssignmentReturn
            && other.Status == WarehouseTransferTaskStatus.Completed
            && other.OriginTaskId == originTaskId);

    private static List<ProductionTransferHeaderLink> BuildResidualChain(
        ProductionTransferHeaderLink root,
        IReadOnlyDictionary<long, ProductionTransferHeaderLink> byHeaderId)
    {
        var chain = new List<ProductionTransferHeaderLink> { root };
        var current = root;
        while (current.ResidualWarehouseTransferHeaderId is long residualId
            && byHeaderId.TryGetValue(residualId, out var next))
        {
            chain.Add(next);
            current = next;
        }

        return chain;
    }
}
