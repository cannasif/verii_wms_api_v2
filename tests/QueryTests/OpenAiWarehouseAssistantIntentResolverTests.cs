using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class OpenAiWarehouseAssistantIntentResolverTests
{
    [Fact]
    public async Task External_intent_request_disables_storage_and_forces_a_strict_read_only_contract()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var resolver = new OpenAiWarehouseAssistantIntentResolver(
            httpClient,
            Options.Create(new WarehouseAssistantOptions
            {
                EnableOpenAiIntentResolution = true,
                ApiKey = "test-key",
                Model = "test-model"
            }),
            new WarehouseAssistantIntentResolver(),
            NullLogger<OpenAiWarehouseAssistantIntentResolver>.Instance);

        var result = await resolver.ResolveAsync("ambiguous warehouse question", null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.False(string.IsNullOrWhiteSpace(handler.RequestBody));
        Assert.False(string.IsNullOrWhiteSpace(handler.ClientRequestId));
        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.Equal(300, root.GetProperty("max_output_tokens").GetInt32());
        var tool = root.GetProperty("tools")[0];
        Assert.True(tool.GetProperty("strict").GetBoolean());
        Assert.False(tool.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("confidence", tool.GetProperty("parameters").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("requiresClarification", tool.GetProperty("parameters").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Hybrid_router_uses_semantic_intent_for_natural_language_instead_of_early_keyword_result()
    {
        var handler = new CaptureHandler(SemanticResponse(
            WarehouseAssistantIntent.ShiftBrief,
            confidence: 0.94m));
        using var httpClient = new HttpClient(handler);
        var resolver = CreateResolver(httpClient);

        var result = await resolver.ResolveAsync("Bugün yaptığım işlere bakınca önce neye yetişmem gerekiyor?", null);

        Assert.Equal(WarehouseAssistantIntent.ShiftBrief, result.Intent);
        Assert.Equal("semantic-v2", result.ProviderMode);
        Assert.NotNull(handler.RequestBody);
    }

    [Fact]
    public async Task Low_confidence_semantic_result_requests_clarification_without_executing_a_wrong_query()
    {
        var handler = new CaptureHandler(SemanticResponse(
            WarehouseAssistantIntent.StockLocationBalance,
            confidence: 0.51m,
            requiresClarification: true,
            clarificationQuestion: "Hangi stok kodunu veya stok adını arıyorsunuz?"));
        using var httpClient = new HttpClient(handler);
        var resolver = CreateResolver(httpClient);

        var result = await resolver.ResolveAsync("Şu malzeme nerede kaldı?", null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.Equal("semantic-clarification-v2", result.ProviderMode);
        Assert.Equal("Hangi stok kodunu veya stok adını arıyorsunuz?", result.ClarificationQuestion);
    }

    [Fact]
    public async Task Hybrid_router_keeps_exact_barcode_lookup_on_zero_latency_fast_path()
    {
        var handler = new CaptureHandler(SemanticResponse(WarehouseAssistantIntent.Unknown, 0.20m));
        using var httpClient = new HttpClient(handler);
        var resolver = CreateResolver(httpClient);

        var result = await resolver.ResolveAsync("Barkod STK-1/SER-1/// sorgula", null);

        Assert.Equal(WarehouseAssistantIntent.BarcodeLookup, result.Intent);
        Assert.Equal("deterministic-fast-path", result.ProviderMode);
        Assert.Null(handler.RequestBody);
    }

    private static OpenAiWarehouseAssistantIntentResolver CreateResolver(HttpClient httpClient) => new(
        httpClient,
        Options.Create(new WarehouseAssistantOptions
        {
            EnableOpenAiIntentResolution = true,
            ApiKey = "test-key",
            Model = "test-model",
            RoutingStrategy = WarehouseAssistantRoutingStrategy.Hybrid,
            MinimumSemanticConfidence = 0.72m
        }),
        new WarehouseAssistantIntentResolver(),
        NullLogger<OpenAiWarehouseAssistantIntentResolver>.Instance);

    private static string SemanticResponse(
        WarehouseAssistantIntent intent,
        decimal confidence,
        bool requiresClarification = false,
        string? clarificationQuestion = null)
    {
        var arguments = JsonSerializer.Serialize(new
        {
            intent = intent.ToString(),
            datePreset = WarehouseAssistantDatePreset.Today.ToString(),
            serialNo = (string?)null,
            stockQuery = (string?)null,
            barcode = (string?)null,
            targetUserQuery = (string?)null,
            requestsAllUsers = false,
            dateFrom = (string?)null,
            dateTo = (string?)null,
            supplierQuery = (string?)null,
            vehiclePlateQuery = (string?)null,
            transferDocumentQuery = (string?)null,
            transferScope = WarehouseAssistantTransferScope.All.ToString(),
            documentQuery = (string?)null,
            confidence,
            requiresClarification,
            clarificationQuestion
        });
        return JsonSerializer.Serialize(new { output = new[] { new { type = "function_call", arguments } } });
    }

    private sealed class CaptureHandler(string responseBody = "{\"output\":[]}") : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? ClientRequestId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            ClientRequestId = request.Headers.GetValues("X-Client-Request-Id").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
