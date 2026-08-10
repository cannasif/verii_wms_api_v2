using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

/// <summary>
/// Combines local semantic similarity with the existing deterministic rules and entity
/// evidence. The result is still only an intent plan; authorization and data access stay
/// in WarehouseAssistantService.
/// </summary>
internal sealed class LocalHybridWarehouseAssistantIntentResolver(
    WarehouseAssistantIntentResolver deterministicResolver,
    ILocalWarehouseSemanticMatcher semanticMatcher,
    IOptions<WarehouseAssistantOptions> options,
    ILogger<LocalHybridWarehouseAssistantIntentResolver> logger)
    : IWarehouseAssistantIntentResolver, IWarehouseAssistantRoutingDiagnostics
{
    private readonly WarehouseAssistantOptions settings = options.Value;

    public WarehouseAssistantRoutingInfo GetRoutingInfo() => new(
        settings.Version,
        semanticMatcher.IsConfigured ? "LocalHybrid" : "LocalSemantic",
        semanticMatcher.IsConfigured,
        semanticMatcher.ModelName);

    public async Task<WarehouseAssistantIntentResolution> ResolveAsync(
        string message,
        WarehouseAssistantContext? context,
        CancellationToken cancellationToken = default)
    {
        var deterministic = await deterministicResolver.ResolveAsync(message, context, cancellationToken);
        var normalized = WarehouseAssistantIntentResolver.Normalize(message);
        var ruleDecision = LocalWarehouseLanguageEngine.Resolve(normalized, deterministic.RequestsAllUsers);

        if (ruleDecision.IsWriteRequest)
            return deterministic with { ProviderMode = "local-policy-write-rejected-v2.4" };
        if (settings.BypassSemanticForExactLookups && IsExactFastPath(deterministic))
            return deterministic with { ProviderMode = "local-deterministic-fast-path-v2.4" };
        if (!semanticMatcher.IsConfigured)
            return deterministic with { ProviderMode = "local-rule-fallback-v2.4" };

        var match = await semanticMatcher.MatchAsync(message, cancellationToken);
        if (!match.IsAvailable || match.Candidates.Count == 0)
            return deterministic with { ProviderMode = "local-rule-fallback-v2.4" };

        var embedding = settings.LocalEmbeddings;
        var candidateIntents = match.Candidates.Select(item => item.Intent)
            .Append(deterministic.Intent)
            .Where(intent => intent is not WarehouseAssistantIntent.Unknown and not WarehouseAssistantIntent.Composite)
            .Distinct()
            .ToArray();
        var scored = candidateIntents
            .Select(intent => Score(intent, match.Candidates, deterministic, ruleDecision, context, normalized, embedding))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.SemanticSimilarity)
            .ThenBy(item => item.Intent)
            .ToArray();
        if (scored.Length == 0)
            return deterministic with { ProviderMode = "local-rule-fallback-v2.4" };

        var best = scored[0];
        var second = scored.Length > 1 ? scored[1] : null;
        var margin = second is null ? best.Score : best.Score - second.Score;
        var belowThreshold = best.Score < embedding.MinimumHybridConfidence
            || best.SemanticSimilarity < embedding.MinimumSemanticSimilarity;
        var ambiguous = second is not null && margin < embedding.AmbiguityMargin;

        if (belowThreshold || ambiguous)
        {
            logger.LogDebug(
                "Local warehouse hybrid routing retained rule result. Semantic intent {SemanticIntent}, score {Score}, margin {Margin}.",
                best.Intent,
                best.Score,
                margin);
            return deterministic with
            {
                ProviderMode = ambiguous ? "local-hybrid-ambiguous-rule-v2.4" : "local-hybrid-low-confidence-rule-v2.4"
            };
        }

        var resolved = EnrichForIntent(deterministic, best.Intent, message, context);
        logger.LogDebug(
            "Local warehouse hybrid routing selected {Intent} with score {Score} and semantic similarity {Similarity}.",
            best.Intent,
            best.Score,
            best.SemanticSimilarity);
        return resolved with
        {
            Confidence = Math.Clamp(best.Score, 0m, 0.99m),
            ProviderMode = best.Intent == deterministic.Intent
                ? "local-hybrid-confirmed-v2.4"
                : "local-hybrid-semantic-v2.4"
        };
    }

    private static HybridCandidate Score(
        WarehouseAssistantIntent intent,
        IReadOnlyList<LocalWarehouseSemanticCandidate> semanticCandidates,
        WarehouseAssistantIntentResolution deterministic,
        LocalWarehouseIntentDecision ruleDecision,
        WarehouseAssistantContext? context,
        string normalizedMessage,
        LocalWarehouseEmbeddingOptions options)
    {
        var semanticSimilarity = semanticCandidates.FirstOrDefault(item => item.Intent == intent)?.Similarity ?? -1m;
        var semanticEvidence = CalibrateSemantic(semanticSimilarity, options);
        var ruleEvidence = intent == deterministic.Intent
            ? deterministic.Confidence
            : intent == ruleDecision.Intent
                ? ruleDecision.Confidence
                : 0m;
        var entityEvidence = EntityEvidence(intent, deterministic, context, normalizedMessage);
        var score = semanticEvidence * options.SemanticWeight
            + ruleEvidence * options.RuleWeight
            + entityEvidence * options.EntityWeight;
        return new HybridCandidate(intent, Math.Clamp(score, 0m, 1m), semanticSimilarity);
    }

    private static decimal CalibrateSemantic(decimal similarity, LocalWarehouseEmbeddingOptions options)
    {
        if (similarity <= options.MinimumSemanticSimilarity)
            return 0m;
        var range = options.StrongSemanticSimilarity - options.MinimumSemanticSimilarity;
        return range <= 0m ? 0m : Math.Clamp((similarity - options.MinimumSemanticSimilarity) / range, 0m, 1m);
    }

    private static decimal EntityEvidence(
        WarehouseAssistantIntent intent,
        WarehouseAssistantIntentResolution resolution,
        WarehouseAssistantContext? context,
        string normalizedMessage)
    {
        var hasCode = Regex.IsMatch(normalizedMessage, @"\b[a-z0-9]+(?:[-/._][a-z0-9]+)+\b", RegexOptions.CultureInvariant);
        return intent switch
        {
            WarehouseAssistantIntent.SerialBalance or WarehouseAssistantIntent.SerialReceiptHistory =>
                !string.IsNullOrWhiteSpace(resolution.SerialNo ?? context?.SerialNo) ? 1m : hasCode ? 0.45m : 0.10m,
            WarehouseAssistantIntent.BarcodeLookup =>
                !string.IsNullOrWhiteSpace(resolution.Barcode ?? context?.Barcode) ? 1m : hasCode ? 0.60m : 0.10m,
            WarehouseAssistantIntent.StockLocationBalance or WarehouseAssistantIntent.StockMovementHistory =>
                !string.IsNullOrWhiteSpace(resolution.StockQuery ?? context?.StockCode) ? 1m : hasCode ? 0.80m : 0.45m,
            WarehouseAssistantIntent.GoodsReceiptAnalysis =>
                !string.IsNullOrWhiteSpace(resolution.SupplierQuery ?? context?.SupplierCode) || resolution.HasExplicitDateFilter ? 1m : 0.65m,
            WarehouseAssistantIntent.SteelVehicleAnalysis =>
                !string.IsNullOrWhiteSpace(resolution.VehiclePlateQuery ?? context?.VehiclePlate) ? 1m : 0.70m,
            WarehouseAssistantIntent.WarehouseTransferAnalysis =>
                !string.IsNullOrWhiteSpace(resolution.TransferDocumentQuery ?? context?.TransferDocumentNo) ? 1m : 0.70m,
            WarehouseAssistantIntent.Traceability =>
                !string.IsNullOrWhiteSpace(resolution.SerialNo ?? context?.SerialNo)
                || !string.IsNullOrWhiteSpace(resolution.Barcode ?? context?.Barcode) ? 1m : hasCode ? 0.65m : 0.10m,
            WarehouseAssistantIntent.ProcessBlockers =>
                !string.IsNullOrWhiteSpace(resolution.DocumentQuery ?? context?.DocumentNo) ? 1m : hasCode ? 0.65m : 0.25m,
            WarehouseAssistantIntent.UserActivities =>
                resolution.RequestsAllUsers || !string.IsNullOrWhiteSpace(resolution.TargetUserQuery) ? 1m : 0.20m,
            WarehouseAssistantIntent.MyActivities =>
                resolution.RequestsAllUsers ? 0.20m : 1m,
            _ => 1m
        };
    }

    private static WarehouseAssistantIntentResolution EnrichForIntent(
        WarehouseAssistantIntentResolution resolution,
        WarehouseAssistantIntent intent,
        string message,
        WarehouseAssistantContext? context) => resolution with
    {
        Intent = intent,
        StockQuery = intent is WarehouseAssistantIntent.StockLocationBalance or WarehouseAssistantIntent.StockMovementHistory
            ? FirstNonBlank(resolution.StockQuery, context?.StockCode, message)
            : resolution.StockQuery,
        SupplierQuery = intent == WarehouseAssistantIntent.GoodsReceiptAnalysis
            ? FirstNonBlank(resolution.SupplierQuery, context?.SupplierCode, message)
            : resolution.SupplierQuery,
        SerialNo = intent is WarehouseAssistantIntent.SerialBalance
            or WarehouseAssistantIntent.SerialReceiptHistory
            or WarehouseAssistantIntent.StockMovementHistory
            or WarehouseAssistantIntent.Traceability
                ? FirstNonBlank(resolution.SerialNo, context?.SerialNo)
                : resolution.SerialNo,
        Barcode = intent is WarehouseAssistantIntent.BarcodeLookup or WarehouseAssistantIntent.Traceability
            ? FirstNonBlank(resolution.Barcode, context?.Barcode)
            : resolution.Barcode,
        AdditionalQueries = null
    };

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool IsExactFastPath(WarehouseAssistantIntentResolution resolution) => resolution.Intent switch
    {
        WarehouseAssistantIntent.Help => true,
        WarehouseAssistantIntent.BarcodeLookup => !string.IsNullOrWhiteSpace(resolution.Barcode),
        WarehouseAssistantIntent.SerialBalance or WarehouseAssistantIntent.SerialReceiptHistory => !string.IsNullOrWhiteSpace(resolution.SerialNo),
        WarehouseAssistantIntent.Traceability => !string.IsNullOrWhiteSpace(resolution.SerialNo) || !string.IsNullOrWhiteSpace(resolution.Barcode),
        WarehouseAssistantIntent.ProcessBlockers => !string.IsNullOrWhiteSpace(resolution.DocumentQuery),
        WarehouseAssistantIntent.SteelVehicleAnalysis => !string.IsNullOrWhiteSpace(resolution.VehiclePlateQuery),
        WarehouseAssistantIntent.WarehouseTransferAnalysis => !string.IsNullOrWhiteSpace(resolution.TransferDocumentQuery),
        _ => false
    };

    private sealed record HybridCandidate(
        WarehouseAssistantIntent Intent,
        decimal Score,
        decimal SemanticSimilarity);
}
