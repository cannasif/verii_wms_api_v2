using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

/// <summary>
/// Lightweight, deterministic conversation planner. It runs entirely in the API process:
/// no LLM, embedding model, Python process or external HTTP service is required.
/// It only plans read-only intents; authorization and database access remain in
/// <see cref="WarehouseAssistantService"/>.
/// </summary>
internal sealed partial class LocalHybridWarehouseAssistantIntentResolver(
    WarehouseAssistantIntentResolver deterministicResolver,
    IOptions<WarehouseAssistantOptions> options,
    ILogger<LocalHybridWarehouseAssistantIntentResolver> logger)
    : IWarehouseAssistantIntentResolver, IWarehouseAssistantRoutingDiagnostics
{
    private const string FastProviderMode = "local-inprocess-fast-v2.8";
    private const string ConversationProviderMode = "local-inprocess-conversation-v2.8";
    private const string CompoundProviderMode = "local-inprocess-compound-v2.8";
    private const string WriteRejectedProviderMode = "local-policy-write-rejected-v2.8";

    private static readonly string[] StrongCompoundConnectors =
    [
        "ayrıca", "ayrica", "bir de", "bunun yanında", "bunun yaninda", "onun yanında", "onun yaninda",
        "ve aynı zamanda", "ve ayni zamanda", "diğer taraftan", "diger taraftan"
    ];

    private readonly WarehouseAssistantOptions settings = options.Value;

    public WarehouseAssistantRoutingInfo GetRoutingInfo() => new(
        settings.Version,
        "InProcessNlp",
        false,
        null);

    public async Task<WarehouseAssistantIntentResolution> ResolveAsync(
        string message,
        WarehouseAssistantContext? context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var boundedMessage = BoundMessage(message, settings.MaximumMessageCharacters);
        var original = await deterministicResolver.ResolveAsync(boundedMessage, context, cancellationToken);
        var policy = LocalWarehouseLanguageEngine.Resolve(
            WarehouseAssistantIntentResolver.Normalize(boundedMessage),
            original.RequestsAllUsers);

        if (policy.IsWriteRequest)
            return original with
            {
                Intent = WarehouseAssistantIntent.Unknown,
                Confidence = 1m,
                ProviderMode = WriteRejectedProviderMode,
                AdditionalQueries = null,
                QueryKind = WarehouseAssistantQueryKind.None
            };

        var rewrite = RewriteConversation(boundedMessage, context);
        var primary = rewrite.Changed
            ? await deterministicResolver.ResolveAsync(rewrite.Message, rewrite.Context, cancellationToken)
            : original;

        var resolvedSegments = await ResolveSegmentsAsync(
            rewrite.Message,
            rewrite.Context,
            cancellationToken);

        if (resolvedSegments.Count > 1)
        {
            var selected = resolvedSegments
                .Where(IsExecutable)
                .DistinctBy(QuerySignature, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(settings.MaximumQueriesPerMessage, 1, 3))
                .ToArray();
            if (selected.Length > 1)
            {
                logger.LogDebug(
                    "In-process warehouse conversation planner produced {QueryCount} read-only queries.",
                    selected.Length);
                return selected[0] with
                {
                    ProviderMode = CompoundProviderMode,
                    AdditionalQueries = selected.Skip(1)
                        .Select(item => item with
                        {
                            ProviderMode = CompoundProviderMode,
                            AdditionalQueries = null
                        })
                        .ToArray()
                };
            }
        }

        var final = resolvedSegments.FirstOrDefault(IsExecutable) ?? primary;
        return final with
        {
            ProviderMode = rewrite.Changed ? ConversationProviderMode : FastProviderMode,
            AdditionalQueries = null
        };
    }

    private async Task<IReadOnlyList<WarehouseAssistantIntentResolution>> ResolveSegmentsAsync(
        string message,
        WarehouseAssistantContext? context,
        CancellationToken cancellationToken)
    {
        var hardSegments = SplitStrongSegments(message)
            .Take(Math.Clamp(settings.MaximumConversationSegments, 2, 10))
            .ToArray();
        if (hardSegments.Length > 1)
        {
            var hardResults = new List<WarehouseAssistantIntentResolution>(hardSegments.Length);
            foreach (var segment in hardSegments)
            {
                var enriched = EnrichEllipticalSegment(segment, message, context);
                var result = await deterministicResolver.ResolveAsync(enriched, context, cancellationToken);
                if (IsExecutable(result)) hardResults.Add(result);
            }
            if (hardResults.Select(item => item.Intent).Distinct().Count() > 1)
                return hardResults;
        }

        var conjunctions = Regex.Matches(
            message,
            @"\s+ve\s+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Cast<Match>();
        foreach (var conjunction in conjunctions)
        {
            if (conjunction.Index <= 0) continue;
            var left = message[..conjunction.Index].Trim(' ', ',', '.', ';', ':');
            var right = message[(conjunction.Index + conjunction.Length)..].Trim(' ', ',', '.', ';', ':');
            if (left.Length < 4 || right.Length < 4) continue;

            var leftResult = await deterministicResolver.ResolveAsync(
                EnrichEllipticalSegment(left, message, context), context, cancellationToken);
            var rightResult = await deterministicResolver.ResolveAsync(
                EnrichEllipticalSegment(right, message, context), context, cancellationToken);
            if (IsExecutable(leftResult)
                && IsExecutable(rightResult)
                && leftResult.Intent != rightResult.Intent)
                return [leftResult, rightResult];
        }

        var single = await deterministicResolver.ResolveAsync(
            EnrichEllipticalSegment(message, message, context), context, cancellationToken);
        return [single];
    }

    private static ConversationRewrite RewriteConversation(string message, WarehouseAssistantContext? context)
    {
        var normalized = WarehouseAssistantIntentResolver.Normalize(message);
        var clearSerial = IsNegated(normalized, "seri", "serial");
        var clearStock = IsNegated(normalized, "stok", "urun", "malzeme", "mamul");
        var clearSupplier = IsNegated(normalized, "cari", "tedarikci", "firma");
        var clearPlate = IsNegated(normalized, "plaka", "arac", "tir");
        var clearBarcode = IsNegated(normalized, "barkod", "etiket");
        var clearDocument = IsNegated(normalized, "belge", "irsaliye", "emir");
        var clearDate = Regex.IsMatch(
            normalized,
            @"\b(?:dun|bugun|gecen hafta|bu hafta|bu ay|gecen ay)\s+degil\b",
            RegexOptions.CultureInvariant);
        var changed = clearSerial || clearStock || clearSupplier || clearPlate || clearBarcode || clearDocument || clearDate;

        var effective = RemoveNegatedPhrases(message);
        var pivot = CorrectionPivotRegex().Matches(effective).Cast<Match>().LastOrDefault();
        if (pivot is not null)
        {
            var boundary = pivot.Index + pivot.Length;
            if (boundary >= 0 && boundary < effective.Length)
            {
                var tail = effective[boundary..].Trim(' ', ',', '.', ';', ':', '-');
                if (tail.Length >= 3)
                {
                    effective = tail;
                    changed = true;
                }
            }
        }

        var rewrittenContext = context is null ? null : context with
        {
            SerialNo = clearSerial ? null : context.SerialNo,
            StockId = clearStock ? null : context.StockId,
            StockCode = clearStock ? null : context.StockCode,
            SupplierId = clearSupplier ? null : context.SupplierId,
            SupplierCode = clearSupplier ? null : context.SupplierCode,
            SupplierName = clearSupplier ? null : context.SupplierName,
            VehiclePlate = clearPlate ? null : context.VehiclePlate,
            Barcode = clearBarcode ? null : context.Barcode,
            DocumentNo = clearDocument ? null : context.DocumentNo,
            TransferDocumentNo = clearDocument ? null : context.TransferDocumentNo,
            DateFrom = clearDate ? null : context.DateFrom,
            DateTo = clearDate ? null : context.DateTo,
            LastDatePreset = clearDate ? null : context.LastDatePreset,
            LastIntent = ShouldClearLastIntent(context.LastIntent, clearSerial, clearStock, clearSupplier, clearPlate, clearBarcode, clearDocument)
                ? null
                : context.LastIntent
        };

        var effectiveNormalized = WarehouseAssistantIntentResolver.Normalize(effective);
        if (clearSerial
            && !string.IsNullOrWhiteSpace(rewrittenContext?.StockCode)
            && !HasStockSubject(effectiveNormalized))
        {
            effective = $"{effective} stok {rewrittenContext.StockCode}";
            changed = true;
        }
        if (clearStock
            && !string.IsNullOrWhiteSpace(rewrittenContext?.SerialNo)
            && !HasSerialSubject(effectiveNormalized))
        {
            effective = $"{effective} seri {rewrittenContext.SerialNo}";
            changed = true;
        }

        return new ConversationRewrite(effective.Trim(), rewrittenContext, changed);
    }

    private static IEnumerable<string> SplitStrongSegments(string message)
    {
        var separatorPattern = string.Join('|', StrongCompoundConnectors.Select(Regex.Escape));
        var pattern = $@"(?:[;\r\n]+|\b(?:{separatorPattern})\b)";
        return Regex.Split(
                message,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(segment => segment.Trim(' ', ',', '.', ';', ':'))
            .Where(segment => segment.Length >= 3);
    }

    private static string EnrichEllipticalSegment(
        string segment,
        string fullMessage,
        WarehouseAssistantContext? context)
    {
        var normalized = WarehouseAssistantIntentResolver.Normalize(segment);
        var hasCode = CodeLikeRegex().IsMatch(segment);
        var hasStockSubject = HasStockSubject(normalized);
        var hasSerialSubject = HasSerialSubject(normalized);
        var needsStockOrSerial = new LocalWarehouseQuestion(normalized).HasAny(
            "hareket", "giris cikis", "nereden nereye", "nerede", "hangi raf", "hangi depo",
            "bakiye", "miktar", "ne kadar", "elde", "kalmis", "mevcut");

        if (needsStockOrSerial && !hasCode && !hasStockSubject && !hasSerialSubject)
        {
            if (!string.IsNullOrWhiteSpace(context?.SerialNo))
                return $"{segment} seri {context.SerialNo}";
            if (!string.IsNullOrWhiteSpace(context?.StockCode))
                return $"{segment} stok {context.StockCode}";
            var sharedCode = ExtractSharedCode(fullMessage);
            if (!string.IsNullOrWhiteSpace(sharedCode))
            {
                var fullNormalized = WarehouseAssistantIntentResolver.Normalize(fullMessage);
                return HasSerialSubject(fullNormalized)
                    ? $"{segment} seri {sharedCode}"
                    : $"{segment} stok {sharedCode}";
            }
        }

        var question = new LocalWarehouseQuestion(normalized);
        if (question.HasAny("mal kabul", "irsaliye", "gelen", "gelmis", "alindi", "almisiz")
            && !question.HasAny("cari", "tedarikci", "firma", "satici")
            && !string.IsNullOrWhiteSpace(context?.SupplierCode))
            return $"{segment} cari {context.SupplierCode}";

        if (question.HasAny("sac", "levha", "arac", "tir", "plaka")
            && !PlateRegex().IsMatch(segment)
            && !string.IsNullOrWhiteSpace(context?.VehiclePlate))
            return $"{segment} plaka {context.VehiclePlate}";

        if (question.HasAny("neden", "niye", "hangi adimda", "neden bekliyor")
            && !CodeLikeRegex().IsMatch(segment)
            && !string.IsNullOrWhiteSpace(context?.DocumentNo))
            return $"{segment} belge {context.DocumentNo}";

        return segment;
    }

    private static string? ExtractSharedCode(string message) => CodeLikeRegex()
        .Matches(message)
        .Select(match => match.Value)
        .FirstOrDefault(value => !LooksLikeDate(value));

    private static bool LooksLikeDate(string value) =>
        Regex.IsMatch(value, @"^\d{1,4}[./-]\d{1,2}[./-]\d{1,4}$", RegexOptions.CultureInvariant);

    private static bool IsNegated(string normalized, params string[] subjects)
    {
        foreach (var subject in subjects)
        {
            var escaped = Regex.Escape(WarehouseAssistantIntentResolver.Normalize(subject));
            if (Regex.IsMatch(
                    normalized,
                    $@"\b{escaped}\w*\s+(?:degil|kastetmedim|istemiyorum|yanlis)\b",
                    RegexOptions.CultureInvariant))
                return true;
        }
        return false;
    }

    private static string RemoveNegatedPhrases(string value) => Regex.Replace(
        value,
        @"\b(?:seri\w*|serial\w*|stok\w*|ürün\w*|urun\w*|malzeme\w*|mamul\w*|cari\w*|tedarikçi\w*|tedarikci\w*|firma\w*|plaka\w*|araç\w*|arac\w*|tır\w*|tir\w*|barkod\w*|etiket\w*|belge\w*|irsaliye\w*|emir\w*|dün|dun|bugün|bugun|geçen\s+hafta|gecen\s+hafta|bu\s+hafta|bu\s+ay|geçen\s+ay|gecen\s+ay)\s+(?:değil|degil|kastetmedim|istemiyorum|yanlış|yanlis)\b",
        " ",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ShouldClearLastIntent(
        WarehouseAssistantIntent? lastIntent,
        bool clearSerial,
        bool clearStock,
        bool clearSupplier,
        bool clearPlate,
        bool clearBarcode,
        bool clearDocument) => lastIntent switch
    {
        WarehouseAssistantIntent.SerialBalance or WarehouseAssistantIntent.SerialReceiptHistory => clearSerial,
        WarehouseAssistantIntent.StockLocationBalance or WarehouseAssistantIntent.StockMovementHistory => clearStock,
        WarehouseAssistantIntent.GoodsReceiptAnalysis => clearSupplier,
        WarehouseAssistantIntent.SteelVehicleAnalysis => clearPlate,
        WarehouseAssistantIntent.BarcodeLookup or WarehouseAssistantIntent.Traceability => clearBarcode || clearSerial,
        WarehouseAssistantIntent.ProcessBlockers => clearDocument,
        _ => false
    };

    private static bool HasStockSubject(string normalized) =>
        new LocalWarehouseQuestion(normalized).HasAny("stok", "urun", "malzeme", "mamul", "parca");

    private static bool HasSerialSubject(string normalized) =>
        new LocalWarehouseQuestion(normalized).HasAny("seri", "serial");

    private static bool IsExecutable(WarehouseAssistantIntentResolution resolution) =>
        resolution.Intent is not WarehouseAssistantIntent.Unknown and not WarehouseAssistantIntent.Composite;

    private static string QuerySignature(WarehouseAssistantIntentResolution resolution) => string.Join('|',
        resolution.Intent,
        resolution.SerialNo,
        resolution.StockQuery,
        resolution.Barcode,
        resolution.SupplierQuery,
        resolution.VehiclePlateQuery,
        resolution.TransferDocumentQuery,
        resolution.DocumentQuery,
        resolution.DatePreset,
        resolution.DateFrom,
        resolution.DateTo);

    private static string BoundMessage(string message, int maximumCharacters)
    {
        var trimmed = (message ?? string.Empty).Trim();
        var maximum = Math.Clamp(maximumCharacters, 200, 4_000);
        return trimmed.Length <= maximum ? trimmed : trimmed[..maximum];
    }

    [GeneratedRegex(@"\b[\p{L}\p{N}]+(?:[-/._][\p{L}\p{N}]+)+\b", RegexOptions.CultureInvariant)]
    private static partial Regex CodeLikeRegex();

    [GeneratedRegex(@"(?<!\d)\d{2}\s*[A-Za-z]{1,3}\s*\d{2,5}(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlateRegex();

    [GeneratedRegex(
        @"(?:daha\s+doğrusu|daha\s+dogrusu|demek\s+istediğim|demek\s+istedigim|kastettiğim|kastettigim|onu\s+değil|onu\s+degil|yanlış\s+söyledim|yanlis\s+soyledim|yanlış\s+yazdım|yanlis\s+yazdim|aslında\s+şöyle|aslinda\s+soyle|düzeltiyorum|duzeltiyorum)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CorrectionPivotRegex();

    private sealed record ConversationRewrite(
        string Message,
        WarehouseAssistantContext? Context,
        bool Changed);
}
