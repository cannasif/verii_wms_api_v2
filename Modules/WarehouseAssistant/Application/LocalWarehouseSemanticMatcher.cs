namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

internal sealed record LocalWarehouseSemanticCandidate(
    WarehouseAssistantIntent Intent,
    decimal Similarity);

internal sealed record LocalWarehouseSemanticMatch(
    bool IsAvailable,
    string? Model,
    IReadOnlyList<LocalWarehouseSemanticCandidate> Candidates)
{
    public static LocalWarehouseSemanticMatch Unavailable(string? model = null) => new(false, model, []);
}

internal interface ILocalWarehouseSemanticMatcher
{
    bool IsConfigured { get; }
    string? ModelName { get; }
    Task<LocalWarehouseSemanticMatch> MatchAsync(string question, CancellationToken cancellationToken = default);
}

/// <summary>
/// Embeds the fixed intent catalog once per process and compares questions using cosine
/// similarity. It never receives or returns operational WMS data.
/// </summary>
internal sealed class LocalWarehouseSemanticMatcher(
    ILocalWarehouseEmbeddingProvider embeddingProvider,
    ILogger<LocalWarehouseSemanticMatcher> logger) : ILocalWarehouseSemanticMatcher
{
    private readonly SemaphoreSlim catalogLock = new(1, 1);
    private IReadOnlyList<CatalogVector>? catalogVectors;

    public bool IsConfigured => embeddingProvider.IsConfigured;
    public string? ModelName => embeddingProvider.ModelName;

    public async Task<LocalWarehouseSemanticMatch> MatchAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(question))
            return LocalWarehouseSemanticMatch.Unavailable(ModelName);

        var catalog = await GetCatalogVectorsAsync(cancellationToken);
        if (catalog is null)
            return LocalWarehouseSemanticMatch.Unavailable(ModelName);

        var queryBatch = await embeddingProvider.EmbedAsync([question], cancellationToken);
        if (queryBatch is null || queryBatch.Vectors.Count != 1)
            return LocalWarehouseSemanticMatch.Unavailable(ModelName);

        var query = queryBatch.Vectors[0];
        var candidates = catalog
            .Where(item => item.Vector.Length == query.Length)
            .GroupBy(item => item.Intent)
            .Select(group => new LocalWarehouseSemanticCandidate(
                group.Key,
                DecimalClamp(group.Select(item => Cosine(query, item.Vector)).OrderByDescending(value => value).Take(2).Average())))
            .OrderByDescending(item => item.Similarity)
            .ThenBy(item => item.Intent)
            .ToArray();

        return candidates.Length == 0
            ? LocalWarehouseSemanticMatch.Unavailable(ModelName)
            : new LocalWarehouseSemanticMatch(true, queryBatch.Model, candidates);
    }

    private async Task<IReadOnlyList<CatalogVector>?> GetCatalogVectorsAsync(CancellationToken cancellationToken)
    {
        if (catalogVectors is not null)
            return catalogVectors;

        if (!await catalogLock.WaitAsync(0, cancellationToken))
            return null;
        try
        {
            if (catalogVectors is not null)
                return catalogVectors;

            var examples = WarehouseAssistantIntentCatalog.Examples;
            var batch = await embeddingProvider.EmbedAsync(examples.Select(item => item.Text).ToArray(), cancellationToken);
            if (batch is null || batch.Vectors.Count != examples.Count)
                return null;

            catalogVectors = examples
                .Select((item, index) => new CatalogVector(item.Intent, batch.Vectors[index]))
                .ToArray();
            logger.LogInformation(
                "Local warehouse semantic catalog initialized with {ExampleCount} examples and model {Model}.",
                catalogVectors.Count,
                batch.Model);
            return catalogVectors;
        }
        finally
        {
            catalogLock.Release();
        }
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        double value = 0;
        for (var index = 0; index < left.Count; index++)
            value += left[index] * right[index];
        return Math.Clamp(value, -1d, 1d);
    }

    private static decimal DecimalClamp(double value) => Math.Clamp((decimal)value, -1m, 1m);

    private sealed record CatalogVector(WarehouseAssistantIntent Intent, float[] Vector);
}
