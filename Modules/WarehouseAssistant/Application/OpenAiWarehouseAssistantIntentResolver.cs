using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed class WarehouseAssistantOptions
{
    public const string SectionName = "WarehouseAssistant";
    public string Version { get; set; } = "2.1.0";
    public bool EnableOpenAiIntentResolution { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-5.6-luna";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;
    public WarehouseAssistantRoutingStrategy RoutingStrategy { get; set; } = WarehouseAssistantRoutingStrategy.Hybrid;
    public decimal MinimumSemanticConfidence { get; set; } = 0.72m;
    public bool BypassSemanticForExactLookups { get; set; } = true;
}
public sealed class OpenAiWarehouseAssistantIntentResolver(
    HttpClient httpClient,
    IOptions<WarehouseAssistantOptions> options,
    WarehouseAssistantIntentResolver fallback,
    ILogger<OpenAiWarehouseAssistantIntentResolver> logger) : IWarehouseAssistantIntentResolver, IWarehouseAssistantRoutingDiagnostics
{
    private readonly WarehouseAssistantOptions settings = options.Value;

    public WarehouseAssistantRoutingInfo GetRoutingInfo()
    {
        var available = IsSemanticRoutingAvailable();
        return new WarehouseAssistantRoutingInfo(
            settings.Version,
            available ? settings.RoutingStrategy.ToString() : "DeterministicOnly",
            available,
            available ? settings.Model : null);
    }

    public async Task<WarehouseAssistantIntentResolution> ResolveAsync(
        string message,
        WarehouseAssistantContext? context,
        CancellationToken cancellationToken = default)
    {
        var deterministic = await fallback.ResolveAsync(message, context, cancellationToken);
        if (!IsSemanticRoutingAvailable() || settings.RoutingStrategy == WarehouseAssistantRoutingStrategy.DeterministicOnly)
            return deterministic;
        if (settings.RoutingStrategy == WarehouseAssistantRoutingStrategy.Hybrid
            && settings.BypassSemanticForExactLookups
            && IsExactDeterministicFastPath(deterministic))
            return deterministic with { ProviderMode = "deterministic-fast-path" };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            request.Headers.TryAddWithoutValidation("X-Client-Request-Id", Guid.NewGuid().ToString("D"));
            var payload = CreatePayload(message, context);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 60)));
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Warehouse assistant intent provider returned HTTP {StatusCode}.", (int)response.StatusCode);
                return deterministic with { ProviderMode = "deterministic-provider-fallback" };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var arguments = TryGetFunctionArguments(document.RootElement);
            if (string.IsNullOrWhiteSpace(arguments))
                return deterministic with { ProviderMode = "deterministic-provider-fallback" };
            using var parsed = JsonDocument.Parse(arguments);
            var root = parsed.RootElement;
            var intent = Enum.TryParse<WarehouseAssistantIntent>(root.GetProperty("intent").GetString(), true, out var resolvedIntent)
                ? resolvedIntent : WarehouseAssistantIntent.Unknown;
            var semanticDatePreset = Enum.TryParse<WarehouseAssistantDatePreset>(root.GetProperty("datePreset").GetString(), true, out var resolvedDate)
                ? resolvedDate : WarehouseAssistantDatePreset.Today;
            var datePreset = deterministic.HasExplicitDateFilter
                ? deterministic.DatePreset
                : semanticDatePreset;
            var confidence = ReadConfidence(root);
            var clarification = SanitizeClarification(root.GetProperty("clarificationQuestion").GetString());
            var requiresClarification = root.GetProperty("requiresClarification").GetBoolean()
                || confidence < Math.Clamp(settings.MinimumSemanticConfidence, 0.50m, 0.95m)
                || intent == WarehouseAssistantIntent.Unknown;
            var dateFrom = ParseDate(root.GetProperty("dateFrom").GetString()) ?? deterministic.DateFrom;
            var dateTo = ParseDate(root.GetProperty("dateTo").GetString()) ?? deterministic.DateTo;
            return new WarehouseAssistantIntentResolution(
                requiresClarification ? WarehouseAssistantIntent.Unknown : intent,
                datePreset,
                NullIfBlank(root.GetProperty("serialNo").GetString()) ?? deterministic.SerialNo ?? context?.SerialNo,
                NullIfBlank(root.GetProperty("stockQuery").GetString()) ?? deterministic.StockQuery ?? context?.StockCode,
                NullIfBlank(root.GetProperty("barcode").GetString()) ?? deterministic.Barcode ?? context?.Barcode,
                NullIfBlank(root.GetProperty("targetUserQuery").GetString()) ?? deterministic.TargetUserQuery,
                root.GetProperty("requestsAllUsers").GetBoolean() || deterministic.RequestsAllUsers,
                confidence,
                requiresClarification ? "semantic-clarification-v2" : "semantic-v2",
                dateFrom,
                dateTo,
                NullIfBlank(root.GetProperty("supplierQuery").GetString()) ?? deterministic.SupplierQuery ?? context?.SupplierCode,
                VehiclePlateQuery: NullIfBlank(root.GetProperty("vehiclePlateQuery").GetString()) ?? deterministic.VehiclePlateQuery ?? context?.VehiclePlate,
                TransferDocumentQuery: NullIfBlank(root.GetProperty("transferDocumentQuery").GetString()) ?? deterministic.TransferDocumentQuery ?? context?.TransferDocumentNo,
                TransferScope: Enum.TryParse<WarehouseAssistantTransferScope>(root.GetProperty("transferScope").GetString(), true, out var transferScope)
                    ? transferScope
                    : context?.TransferScope ?? WarehouseAssistantTransferScope.All,
                HasExplicitDateFilter: deterministic.HasExplicitDateFilter
                    || !string.IsNullOrWhiteSpace(root.GetProperty("dateFrom").GetString())
                    || !string.IsNullOrWhiteSpace(root.GetProperty("dateTo").GetString()),
                DocumentQuery: NullIfBlank(root.GetProperty("documentQuery").GetString()) ?? deterministic.DocumentQuery ?? context?.DocumentNo,
                ClarificationQuestion: requiresClarification ? clarification : null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Warehouse assistant intent provider failed; deterministic resolver will be used.");
            return deterministic with { ProviderMode = "deterministic-provider-fallback" };
        }
    }

    private bool IsSemanticRoutingAvailable() =>
        settings.EnableOpenAiIntentResolution
        && !string.IsNullOrWhiteSpace(settings.ApiKey)
        && !string.IsNullOrWhiteSpace(settings.Model);

    private static bool IsExactDeterministicFastPath(WarehouseAssistantIntentResolution resolution) => resolution.Intent switch
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

    private object CreatePayload(string message, WarehouseAssistantContext? context) => new
    {
        model = settings.Model,
        store = false,
        max_output_tokens = 300,
        input = new object[]
        {
            new { role = "system", content = $"""
You route multilingual warehouse questions to one safe read-only WMS intent. Today is {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.
Intent catalog: MyActivities/UserActivities = who performed which operations; SerialBalance = current serial quantity and location; SerialReceiptHistory = when and by whom a serial entered; StockLocationBalance = where an item is and its quantities; BarcodeLookup = decode or identify a scanned label; StockMovementHistory = inbound/outbound movement ledger; AssignedTasks = work assigned to a user; GoodsReceiptAnalysis = receipt counts/items by supplier/date; ParameterHelp = explain a supplied setting; SteelVehicleAnalysis = steel/sheet vehicle entries and plates; WarehouseTransferAnalysis = inter-warehouse or production transfer reports; ShiftBrief = today's workload summary; OperationalExceptions = failed, overdue, inconsistent or stuck operations; Traceability = end-to-end serial/lot/barcode journey; ProcessBlockers = why a named document cannot progress; Help = supported questions.
Understand meaning rather than exact wording. Treat stock, item, product, material, ürün, mamul and malzeme as synonyms; supplier, vendor, cari and tedarikçi as synonyms; bin, shelf, raf and location as synonyms. Keep explicit date ranges inclusive. Do not decide authorization, generate SQL, invent identifiers or route write requests. If the requested operation is unsupported, the entity is ambiguous, or essential information cannot be inferred from context, set requiresClarification=true and ask one short question in the user's language. Return only the forced function call.
""" },
            new { role = "user", content = $"Question: {message}\nPrevious validated context: {JsonSerializer.Serialize(context)}" }
        },
        tools = new object[]
        {
            new
            {
                type = "function",
                name = "resolve_wms_question",
                description = "Semantically resolve a warehouse question to one safe read-only intent or request clarification.",
                strict = true,
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        intent = new { type = "string", @enum = Enum.GetNames<WarehouseAssistantIntent>() },
                        datePreset = new { type = "string", @enum = Enum.GetNames<WarehouseAssistantDatePreset>() },
                        serialNo = new { type = new[] { "string", "null" } },
                        stockQuery = new { type = new[] { "string", "null" } },
                        barcode = new { type = new[] { "string", "null" } },
                        targetUserQuery = new { type = new[] { "string", "null" } },
                        requestsAllUsers = new { type = "boolean" },
                        dateFrom = new { type = new[] { "string", "null" }, description = "Inclusive date from as yyyy-MM-dd" },
                        dateTo = new { type = new[] { "string", "null" }, description = "Inclusive date to as yyyy-MM-dd" },
                        supplierQuery = new { type = new[] { "string", "null" } },
                        vehiclePlateQuery = new { type = new[] { "string", "null" } },
                        transferDocumentQuery = new { type = new[] { "string", "null" } },
                        transferScope = new { type = "string", @enum = Enum.GetNames<WarehouseAssistantTransferScope>() },
                        documentQuery = new { type = new[] { "string", "null" } },
                        confidence = new { type = "number", minimum = 0, maximum = 1 },
                        requiresClarification = new { type = "boolean" },
                        clarificationQuestion = new { type = new[] { "string", "null" } }
                    },
                    required = new[] { "intent", "datePreset", "serialNo", "stockQuery", "barcode", "targetUserQuery", "requestsAllUsers", "dateFrom", "dateTo", "supplierQuery", "vehiclePlateQuery", "transferDocumentQuery", "transferScope", "documentQuery", "confidence", "requiresClarification", "clarificationQuestion" },
                    additionalProperties = false
                }
            }
        },
        tool_choice = new { type = "function", name = "resolve_wms_question" },
        parallel_tool_calls = false
    };

    private static string? TryGetFunctionArguments(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "function_call") continue;
            if (item.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.String)
                return arguments.GetString();
        }
        return null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal ReadConfidence(JsonElement root) =>
        root.TryGetProperty("confidence", out var property) && property.TryGetDecimal(out var value)
            ? Math.Clamp(value, 0m, 1m)
            : 0m;

    private static string? SanitizeClarification(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(x => !char.IsControl(x)).ToArray()).Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240].TrimEnd();
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date) ? date : null;
}
