namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed partial class WarehouseAssistantService
{
    private static IReadOnlyList<WarehouseAssistantIntentResolution> ExpandQueryPlan(
        WarehouseAssistantIntentResolution resolution)
    {
        var queries = new List<WarehouseAssistantIntentResolution>(3)
        {
            resolution with { AdditionalQueries = null }
        };
        if (resolution.AdditionalQueries is { Count: > 0 })
        {
            queries.AddRange(resolution.AdditionalQueries
                .Take(2)
                .Select(x => x with { AdditionalQueries = null }));
        }
        var invalid = queries.FirstOrDefault(x =>
            x.Intent is WarehouseAssistantIntent.Unknown or WarehouseAssistantIntent.Composite);
        if (invalid is not null) return [invalid];

        return queries
            .DistinctBy(QuerySignature, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
    }

    private static string QuerySignature(WarehouseAssistantIntentResolution query) => string.Join(
        "\u001f",
        query.Intent,
        query.DatePreset,
        query.SerialNo,
        query.StockQuery,
        query.Barcode,
        query.TargetUserQuery,
        query.SupplierQuery,
        query.VehiclePlateQuery,
        query.TransferDocumentQuery,
        query.TransferScope,
        query.DocumentQuery,
        query.DateFrom,
        query.DateTo);

    private async Task<ExecutionResult> ExecuteQueryPlanAsync(
        IReadOnlyList<WarehouseAssistantIntentResolution> queryPlan,
        string originalMessage,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (queryPlan.Count == 1)
            return await ExecuteIntentAsync(queryPlan[0], originalMessage, actorUserId, branchCode, access, ct);

        var results = new List<ExecutionResult>(queryPlan.Count);
        foreach (var query in queryPlan)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ExecuteIntentAsync(query, originalMessage, actorUserId, branchCode, access, ct));
        }
        return MergeExecutionResults(results);
    }

    private static ExecutionResult MergeExecutionResults(IReadOnlyList<ExecutionResult> results)
    {
        var contexts = results.Select(x => x.Context).ToArray();
        return new ExecutionResult(
            WarehouseAssistantIntent.Composite,
            string.Join(";", results.Select(x => x.Scope).Distinct(StringComparer.OrdinalIgnoreCase)),
            "multi-query-plan",
            string.Join(
                Environment.NewLine + Environment.NewLine,
                results.Select(x => x.Answer).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
            Take(results.SelectMany(x => x.Activities)),
            Take(results.SelectMany(x => x.SerialBalances)),
            Take(results.SelectMany(x => x.SerialReceipts)),
            Take(results.SelectMany(x => x.StockLocations)),
            results.Select(x => x.Barcode).FirstOrDefault(x => x is not null),
            Take(results.SelectMany(x => x.Movements)),
            Take(results.SelectMany(x => x.Tasks)),
            MergeExecutionContexts(contexts),
            results.SelectMany(x => x.Suggestions)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Take(12)
                .ToArray(),
            Take(results.SelectMany(x => x.GoodsReceipts ?? [])),
            Take(results.SelectMany(x => x.ParameterGuides ?? [])),
            Take(results.SelectMany(x => x.SteelVehicles ?? [])),
            Take(results.SelectMany(x => x.Transfers ?? [])),
            Take(results.SelectMany(x => x.EntityCandidates ?? [])),
            Take(results.SelectMany(x => x.SummaryMetrics ?? [])),
            Take(results.SelectMany(x => x.Exceptions ?? [])),
            Take(results.SelectMany(x => x.TraceabilityEvents ?? [])));
    }

    private static IReadOnlyList<T> Take<T>(IEnumerable<T> source) =>
        source.Take(MaximumResultCount).ToArray();

    private static WarehouseAssistantContext BuildConversationContext(
        WarehouseAssistantContext? previous,
        WarehouseAssistantContext current,
        IReadOnlyList<WarehouseAssistantIntentResolution> queryPlan,
        string originalMessage,
        WarehouseAssistantIntent resultIntent)
    {
        var merged = OverlayContext(previous, current);
        var successful = resultIntent != WarehouseAssistantIntent.Unknown;
        var primaryIntent = queryPlan[0].Intent;
        var targetUser = queryPlan.Select(x => x.TargetUserQuery).LastOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? current.TargetUserQuery;
        return merged with
        {
            LastIntent = successful
                ? resultIntent == WarehouseAssistantIntent.Composite ? primaryIntent : resultIntent
                : previous?.LastIntent,
            LastResolvedQuestion = successful ? originalMessage : previous?.LastResolvedQuestion,
            PendingQuestion = successful ? null : originalMessage,
            TargetUserQuery = successful ? targetUser : previous?.TargetUserQuery,
            RequestsAllUsers = successful
                ? queryPlan.Any(x => x.RequestsAllUsers) || current.RequestsAllUsers == true
                : previous?.RequestsAllUsers,
            LastDatePreset = successful ? queryPlan[0].DatePreset : previous?.LastDatePreset
        };
    }

    private static WarehouseAssistantContext MergeExecutionContexts(
        IReadOnlyList<WarehouseAssistantContext> contexts)
    {
        WarehouseAssistantContext? merged = null;
        foreach (var context in contexts)
            merged = OverlayContext(merged, context);
        return merged ?? new WarehouseAssistantContext(null, null, null);
    }

    private static WarehouseAssistantContext OverlayContext(
        WarehouseAssistantContext? previous,
        WarehouseAssistantContext current) => new(
        current.SerialNo ?? previous?.SerialNo,
        current.StockId ?? previous?.StockId,
        current.StockCode ?? previous?.StockCode,
        current.Barcode ?? previous?.Barcode,
        current.SupplierId ?? previous?.SupplierId,
        current.SupplierCode ?? previous?.SupplierCode,
        current.SupplierName ?? previous?.SupplierName,
        current.DateFrom ?? previous?.DateFrom,
        current.DateTo ?? previous?.DateTo,
        current.ParameterModule ?? previous?.ParameterModule,
        current.ParameterField ?? previous?.ParameterField,
        current.ParameterValue ?? previous?.ParameterValue,
        current.VehiclePlate ?? previous?.VehiclePlate,
        current.TransferDocumentNo ?? previous?.TransferDocumentNo,
        current.TransferScope ?? previous?.TransferScope,
        current.DocumentNo ?? previous?.DocumentNo,
        current.LastIntent ?? previous?.LastIntent,
        current.LastResolvedQuestion ?? previous?.LastResolvedQuestion,
        current.PendingQuestion ?? previous?.PendingQuestion,
        current.TargetUserQuery ?? previous?.TargetUserQuery,
        current.RequestsAllUsers ?? previous?.RequestsAllUsers,
        current.LastDatePreset ?? previous?.LastDatePreset);
}
