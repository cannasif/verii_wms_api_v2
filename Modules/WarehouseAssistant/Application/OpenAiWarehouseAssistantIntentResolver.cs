using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed class WarehouseAssistantOptions
{
    public const string SectionName = "WarehouseAssistant";
    public bool EnableOpenAiIntentResolution { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;
}
public sealed class OpenAiWarehouseAssistantIntentResolver(
    HttpClient httpClient,
    IOptions<WarehouseAssistantOptions> options,
    WarehouseAssistantIntentResolver fallback,
    ILogger<OpenAiWarehouseAssistantIntentResolver> logger) : IWarehouseAssistantIntentResolver
{
    private readonly WarehouseAssistantOptions settings = options.Value;

    public async Task<WarehouseAssistantIntentResolution> ResolveAsync(
        string message,
        WarehouseAssistantContext? context,
        CancellationToken cancellationToken = default)
    {
        var deterministic = await fallback.ResolveAsync(message, context, cancellationToken);
        if (deterministic.Intent != WarehouseAssistantIntent.Unknown
            || !settings.EnableOpenAiIntentResolution
            || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.Model))
            return deterministic;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            var payload = CreatePayload(message, context);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 60)));
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Warehouse assistant intent provider returned HTTP {StatusCode}.", (int)response.StatusCode);
                return deterministic;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var arguments = document.RootElement.GetProperty("output").EnumerateArray()
                .FirstOrDefault(x => x.TryGetProperty("type", out var type) && type.GetString() == "function_call")
                .GetProperty("arguments").GetString();
            if (string.IsNullOrWhiteSpace(arguments)) return deterministic;
            using var parsed = JsonDocument.Parse(arguments);
            var root = parsed.RootElement;
            var intent = Enum.TryParse<WarehouseAssistantIntent>(root.GetProperty("intent").GetString(), true, out var resolvedIntent)
                ? resolvedIntent : WarehouseAssistantIntent.Unknown;
            var datePreset = Enum.TryParse<WarehouseAssistantDatePreset>(root.GetProperty("datePreset").GetString(), true, out var resolvedDate)
                ? resolvedDate : WarehouseAssistantDatePreset.Today;
            return new WarehouseAssistantIntentResolution(
                intent,
                datePreset,
                NullIfBlank(root.GetProperty("serialNo").GetString()) ?? context?.SerialNo,
                NullIfBlank(root.GetProperty("stockQuery").GetString()) ?? context?.StockCode,
                NullIfBlank(root.GetProperty("targetUserQuery").GetString()),
                root.GetProperty("requestsAllUsers").GetBoolean(),
                0.85m,
                "openai");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Warehouse assistant intent provider failed; deterministic resolver will be used.");
            return deterministic;
        }
    }

    private object CreatePayload(string message, WarehouseAssistantContext? context) => new
    {
        model = settings.Model,
        input = new object[]
        {
            new { role = "system", content = "Classify Turkish WMS questions. Stock, ürün, malzeme and mamul are synonyms. Never decide authorization. Return only the forced function call." },
            new { role = "user", content = $"Question: {message}\nPrevious entity context: {JsonSerializer.Serialize(context)}" }
        },
        tools = new object[]
        {
            new
            {
                type = "function",
                name = "resolve_wms_question",
                description = "Resolve a warehouse assistant question to one safe read-only intent.",
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
                        targetUserQuery = new { type = new[] { "string", "null" } },
                        requestsAllUsers = new { type = "boolean" }
                    },
                    required = new[] { "intent", "datePreset", "serialNo", "stockQuery", "targetUserQuery", "requestsAllUsers" },
                    additionalProperties = false
                }
            }
        },
        tool_choice = new { type = "function", name = "resolve_wms_question" },
        parallel_tool_calls = false
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
