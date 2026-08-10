using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

internal sealed record LocalWarehouseEmbeddingBatch(
    IReadOnlyList<float[]> Vectors,
    string Model);

internal interface ILocalWarehouseEmbeddingProvider
{
    bool IsConfigured { get; }
    string? ModelName { get; }
    Task<LocalWarehouseEmbeddingBatch?> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls only the explicitly configured loopback Ollama endpoint. Questions never leave
/// the WMS host, and failures are isolated so the deterministic policy kernel stays usable.
/// </summary>
internal sealed class OllamaLocalWarehouseEmbeddingProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<WarehouseAssistantOptions> options,
    TimeProvider timeProvider,
    ILogger<OllamaLocalWarehouseEmbeddingProvider> logger) : ILocalWarehouseEmbeddingProvider
{
    internal const string HttpClientName = "WarehouseAssistant.LocalEmbeddings";
    private readonly LocalWarehouseEmbeddingOptions settings = options.Value.LocalEmbeddings;
    private readonly object failureLock = new();
    private DateTimeOffset retryAfterUtc = DateTimeOffset.MinValue;

    public bool IsConfigured => settings.Enabled
        && LocalWarehouseEmbeddingOptions.IsSafeLoopbackEndpoint(settings.Endpoint)
        && !string.IsNullOrWhiteSpace(settings.Model);

    public string? ModelName => IsConfigured ? settings.Model : null;

    public async Task<LocalWarehouseEmbeddingBatch?> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || inputs.Count == 0 || inputs.Count > settings.MaximumBatchSize)
            return null;

        lock (failureLock)
        {
            if (timeProvider.GetUtcNow() < retryAfterUtc)
                return null;
        }

        var sanitizedInputs = inputs
            .Select(value => FormatInput(value, settings.InputPrefix, settings.MaximumInputCharacters))
            .ToArray();
        if (sanitizedInputs.Any(string.IsNullOrWhiteSpace))
            return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds));
            var endpoint = new Uri($"{settings.Endpoint.TrimEnd('/')}/api/embed", UriKind.Absolute);
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(endpoint, new
            {
                model = settings.Model,
                input = sanitizedInputs,
                truncate = true,
                keep_alive = settings.KeepAlive
            }, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                RegisterFailure();
                logger.LogWarning(
                    "Local warehouse embedding provider returned HTTP {StatusCode}; deterministic routing remains active.",
                    (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            if (!TryReadVectors(document.RootElement, sanitizedInputs.Length, out var vectors))
            {
                RegisterFailure();
                logger.LogWarning("Local warehouse embedding provider returned an invalid vector batch.");
                return null;
            }

            lock (failureLock)
                retryAfterUtc = DateTimeOffset.MinValue;
            return new LocalWarehouseEmbeddingBatch(vectors, settings.Model);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            RegisterFailure();
            logger.LogWarning(exception,
                "Local warehouse embedding provider is unavailable; deterministic routing remains active.");
            return null;
        }
    }

    private void RegisterFailure()
    {
        lock (failureLock)
            retryAfterUtc = timeProvider.GetUtcNow().AddSeconds(settings.FailureBackoffSeconds);
    }

    private static string FormatInput(string value, string prefix, int maximumCharacters)
    {
        var clean = new string(value.Where(character => !char.IsControl(character) || char.IsWhiteSpace(character)).ToArray()).Trim();
        var cleanPrefix = new string((prefix ?? string.Empty).Where(character => !char.IsControl(character)).ToArray());
        var available = Math.Max(0, maximumCharacters - cleanPrefix.Length);
        return cleanPrefix + (clean.Length <= available ? clean : clean[..available]);
    }

    private static bool TryReadVectors(JsonElement root, int expectedCount, out IReadOnlyList<float[]> vectors)
    {
        vectors = [];
        if (!root.TryGetProperty("embeddings", out var embeddings) || embeddings.ValueKind != JsonValueKind.Array)
            return false;

        var result = new List<float[]>(expectedCount);
        var dimensions = 0;
        foreach (var embedding in embeddings.EnumerateArray())
        {
            if (embedding.ValueKind != JsonValueKind.Array)
                return false;
            var values = embedding.EnumerateArray()
                .Select(value => value.TryGetSingle(out var number) ? number : float.NaN)
                .ToArray();
            if (values.Length == 0 || values.Any(value => !float.IsFinite(value)))
                return false;
            dimensions = dimensions == 0 ? values.Length : dimensions;
            if (values.Length != dimensions)
                return false;
            if (!Normalize(values))
                return false;
            result.Add(values);
        }

        if (result.Count != expectedCount)
            return false;
        vectors = result;
        return true;
    }

    private static bool Normalize(float[] vector)
    {
        var length = Math.Sqrt(vector.Sum(value => (double)value * value));
        if (length <= double.Epsilon)
            return false;
        for (var index = 0; index < vector.Length; index++)
            vector[index] = (float)(vector[index] / length);
        return true;
    }
}
