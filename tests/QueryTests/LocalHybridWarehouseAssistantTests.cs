using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class LocalHybridWarehouseAssistantTests
{
    [Fact]
    public async Task Semantic_meaning_can_resolve_an_unseen_natural_question()
    {
        var matcher = new FixedSemanticMatcher(
            new LocalWarehouseSemanticCandidate(WarehouseAssistantIntent.ShiftBrief, 0.88m),
            new LocalWarehouseSemanticCandidate(WarehouseAssistantIntent.AssignedTasks, 0.43m));
        var resolver = CreateResolver(matcher);

        var result = await resolver.ResolveAsync(
            "Masanın başına geldim; operasyonel olarak odağımı nereye vermeliyim?",
            null);

        Assert.Equal(WarehouseAssistantIntent.ShiftBrief, result.Intent);
        Assert.Equal("local-hybrid-semantic-v2.4", result.ProviderMode);
        Assert.True(result.Confidence >= 0.50m);
    }

    [Fact]
    public async Task Semantic_goods_receipt_intent_keeps_the_original_phrase_for_safe_entity_resolution()
    {
        var matcher = new FixedSemanticMatcher(
            new LocalWarehouseSemanticCandidate(WarehouseAssistantIntent.GoodsReceiptAnalysis, 0.90m),
            new LocalWarehouseSemanticCandidate(WarehouseAssistantIntent.StockLocationBalance, 0.40m));
        var resolver = CreateResolver(matcher);
        const string question = "Geçtiğimiz ay ASD firmasından depoya ulaşanları özetler misin?";

        var result = await resolver.ResolveAsync(question, null);

        Assert.Equal(WarehouseAssistantIntent.GoodsReceiptAnalysis, result.Intent);
        Assert.Equal(question, result.SupplierQuery);
    }

    [Fact]
    public async Task Close_semantic_candidates_do_not_override_the_safe_rule_result()
    {
        var matcher = new FixedSemanticMatcher(
            new LocalWarehouseSemanticCandidate(WarehouseAssistantIntent.ShiftBrief, 0.70m),
            new LocalWarehouseSemanticCandidate(WarehouseAssistantIntent.AssignedTasks, 0.69m));
        var resolver = CreateResolver(matcher);

        var result = await resolver.ResolveAsync("Depodaki duruma bir bakar mısın?", null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.Equal("local-hybrid-ambiguous-rule-v2.4", result.ProviderMode);
    }

    [Fact]
    public async Task Write_requests_are_rejected_before_the_embedding_provider_is_called()
    {
        var matcher = new FixedSemanticMatcher(
            new LocalWarehouseSemanticCandidate(WarehouseAssistantIntent.WarehouseTransferAnalysis, 0.99m));
        var resolver = CreateResolver(matcher);

        var result = await resolver.ResolveAsync("WT-2026-001 transferini onayla", null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.Equal("local-policy-write-rejected-v2.4", result.ProviderMode);
        Assert.Equal(0, matcher.CallCount);
    }

    [Fact]
    public async Task Exact_barcode_lookup_stays_on_the_zero_latency_policy_path()
    {
        var matcher = new FixedSemanticMatcher(
            new LocalWarehouseSemanticCandidate(WarehouseAssistantIntent.StockLocationBalance, 0.99m));
        var resolver = CreateResolver(matcher);

        var result = await resolver.ResolveAsync("Barkod STK-1/SER-1/// sorgula", null);

        Assert.Equal(WarehouseAssistantIntent.BarcodeLookup, result.Intent);
        Assert.Equal("local-deterministic-fast-path-v2.4", result.ProviderMode);
        Assert.Equal(0, matcher.CallCount);
    }

    [Fact]
    public async Task Ollama_provider_sends_a_bounded_batch_and_normalizes_vectors()
    {
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"model\":\"embeddinggemma\",\"embeddings\":[[3,4],[0,2]]}", Encoding.UTF8, "application/json")
        });
        var settings = new WarehouseAssistantOptions();
        var provider = new OllamaLocalWarehouseEmbeddingProvider(
            new FixedHttpClientFactory(handler),
            Options.Create(settings),
            TimeProvider.System,
            NullLogger<OllamaLocalWarehouseEmbeddingProvider>.Instance);

        var result = await provider.EmbedAsync(["birinci", "ikinci"]);

        Assert.NotNull(result);
        Assert.Equal(2, result.Vectors.Count);
        Assert.Equal(0.6f, result.Vectors[0][0], 3);
        Assert.Equal(0.8f, result.Vectors[0][1], 3);
        Assert.Equal(1f, result.Vectors[1][1], 3);
        Assert.Contains("/api/embed", handler.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("task: classification | query: birinci", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("task: classification | query: ikinci", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_failure_opens_the_backoff_circuit_instead_of_delaying_every_question()
    {
        var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = new OllamaLocalWarehouseEmbeddingProvider(
            new FixedHttpClientFactory(handler),
            Options.Create(new WarehouseAssistantOptions()),
            TimeProvider.System,
            NullLogger<OllamaLocalWarehouseEmbeddingProvider>.Instance);

        var first = await provider.EmbedAsync(["soru"]);
        var second = await provider.EmbedAsync(["başka soru"]);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Semantic_catalog_is_embedded_once_and_reused_between_questions()
    {
        var provider = new CountingEmbeddingProvider();
        var matcher = new LocalWarehouseSemanticMatcher(
            provider,
            NullLogger<LocalWarehouseSemanticMatcher>.Instance);

        var first = await matcher.MatchAsync("ilk soru");
        var second = await matcher.MatchAsync("ikinci soru");

        Assert.True(first.IsAvailable);
        Assert.True(second.IsAvailable);
        Assert.Equal(3, provider.BatchSizes.Count);
        Assert.True(provider.BatchSizes[0] > 1);
        Assert.Equal(1, provider.BatchSizes[1]);
        Assert.Equal(1, provider.BatchSizes[2]);
    }

    private static LocalHybridWarehouseAssistantIntentResolver CreateResolver(ILocalWarehouseSemanticMatcher matcher)
    {
        var settings = new WarehouseAssistantOptions();
        return new LocalHybridWarehouseAssistantIntentResolver(
            new WarehouseAssistantIntentResolver(),
            matcher,
            Options.Create(settings),
            NullLogger<LocalHybridWarehouseAssistantIntentResolver>.Instance);
    }

    private sealed class FixedSemanticMatcher(params LocalWarehouseSemanticCandidate[] candidates)
        : ILocalWarehouseSemanticMatcher
    {
        public int CallCount { get; private set; }
        public bool IsConfigured => true;
        public string? ModelName => "test-embedding";

        public Task<LocalWarehouseSemanticMatch> MatchAsync(string question, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new LocalWarehouseSemanticMatch(true, ModelName, candidates));
        }
    }

    private sealed class CountingEmbeddingProvider : ILocalWarehouseEmbeddingProvider
    {
        public List<int> BatchSizes { get; } = [];
        public bool IsConfigured => true;
        public string? ModelName => "test-embedding";

        public Task<LocalWarehouseEmbeddingBatch?> EmbedAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            BatchSizes.Add(inputs.Count);
            IReadOnlyList<float[]> vectors = inputs.Select(_ => new[] { 1f, 0f }).ToArray();
            return Task.FromResult<LocalWarehouseEmbeddingBatch?>(new LocalWarehouseEmbeddingBatch(vectors, ModelName!));
        }
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class QueueHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
