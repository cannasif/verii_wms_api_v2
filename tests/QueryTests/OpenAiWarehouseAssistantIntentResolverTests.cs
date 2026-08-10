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
    public async Task Disabled_external_provider_uses_only_the_advanced_local_engine()
    {
        var handler = new CaptureHandler();
        using var httpClient = new HttpClient(handler);
        var resolver = new OpenAiWarehouseAssistantIntentResolver(
            httpClient,
            Options.Create(new WarehouseAssistantOptions()),
            new WarehouseAssistantIntentResolver(),
            NullLogger<OpenAiWarehouseAssistantIntentResolver>.Instance);

        var routing = resolver.GetRoutingInfo();
        var result = await resolver.ResolveAsync("01/013 depoda nerelere dağılmış?", null);

        Assert.Equal("2.3.0", routing.Version);
        Assert.Equal("LocalSemantic", routing.RoutingMode);
        Assert.False(routing.SemanticRoutingAvailable);
        Assert.Null(routing.SemanticModel);
        Assert.Equal(WarehouseAssistantIntent.StockLocationBalance, result.Intent);
        Assert.Equal("local-semantic-v2.3", result.ProviderMode);
        Assert.Null(handler.RequestBody);
    }

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
        Assert.Equal(900, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("low", root.GetProperty("reasoning").GetProperty("effort").GetString());
        var tool = root.GetProperty("tools")[0];
        Assert.True(tool.GetProperty("strict").GetBoolean());
        Assert.False(tool.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("confidence", tool.GetProperty("parameters").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("requiresClarification", tool.GetProperty("parameters").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("additionalQueries", tool.GetProperty("parameters").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        var additionalItems = tool.GetProperty("parameters").GetProperty("properties")
            .GetProperty("additionalQueries").GetProperty("items");
        Assert.False(additionalItems.GetProperty("additionalProperties").GetBoolean());
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
        Assert.Equal("semantic-v2.2", result.ProviderMode);
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
        Assert.Equal("semantic-clarification-v2.2", result.ProviderMode);
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

    [Fact]
    public async Task Semantic_router_returns_a_bounded_compound_read_plan()
    {
        var additional = new[]
        {
            SemanticQuery(WarehouseAssistantIntent.AssignedTasks, 0.93m)
        };
        var handler = new CaptureHandler(SemanticResponse(
            WarehouseAssistantIntent.MyActivities,
            confidence: 0.96m,
            additionalQueries: additional));
        using var httpClient = new HttpClient(handler);
        var resolver = CreateResolver(httpClient);

        var result = await resolver.ResolveAsync(
            "Hem bugün yaptığım işlemleri hem de bana atanan açık emirleri getir",
            null);

        Assert.Equal(WarehouseAssistantIntent.MyActivities, result.Intent);
        Assert.Equal("semantic-compound-v2.2", result.ProviderMode);
        var query = Assert.Single(result.AdditionalQueries!);
        Assert.Equal(WarehouseAssistantIntent.AssignedTasks, query.Intent);
    }

    [Fact]
    public async Task Ambiguous_item_in_compound_plan_blocks_the_entire_plan()
    {
        var additional = new[]
        {
            SemanticQuery(
                WarehouseAssistantIntent.Unknown,
                0.40m,
                requiresClarification: true,
                clarificationQuestion: "İkinci soruda hangi malzemeyi kastediyorsunuz?")
        };
        var handler = new CaptureHandler(SemanticResponse(
            WarehouseAssistantIntent.MyActivities,
            confidence: 0.96m,
            additionalQueries: additional));
        using var httpClient = new HttpClient(handler);
        var resolver = CreateResolver(httpClient);

        var result = await resolver.ResolveAsync("İşlemlerimi getir; ayrıca bu malzemeye bak", null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.Null(result.AdditionalQueries);
        Assert.Equal("İkinci soruda hangi malzemeyi kastediyorsunuz?", result.ClarificationQuestion);
    }

    [Fact]
    public async Task Deterministic_router_supports_explicit_compound_questions_without_provider_access()
    {
        using var httpClient = new HttpClient(new CaptureHandler());
        var resolver = new OpenAiWarehouseAssistantIntentResolver(
            httpClient,
            Options.Create(new WarehouseAssistantOptions()),
            new WarehouseAssistantIntentResolver(),
            NullLogger<OpenAiWarehouseAssistantIntentResolver>.Instance);

        var result = await resolver.ResolveAsync(
            "Bugün yaptığım işlemleri göster; ayrıca bana atanan emirleri getir",
            null);

        Assert.Equal(WarehouseAssistantIntent.MyActivities, result.Intent);
        Assert.Equal("local-semantic-compound-v2.3", result.ProviderMode);
        Assert.Equal(WarehouseAssistantIntent.AssignedTasks, Assert.Single(result.AdditionalQueries!).Intent);
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
        string? clarificationQuestion = null,
        object[]? additionalQueries = null)
    {
        var query = SemanticQuery(intent, confidence, requiresClarification, clarificationQuestion);
        var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(query))!;
        values["additionalQueries"] = additionalQueries ?? [];
        var arguments = JsonSerializer.Serialize(values);
        return JsonSerializer.Serialize(new { output = new[] { new { type = "function_call", arguments } } });
    }

    private static object SemanticQuery(
        WarehouseAssistantIntent intent,
        decimal confidence,
        bool requiresClarification = false,
        string? clarificationQuestion = null) => new
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
    };

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
