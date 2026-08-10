using Microsoft.Extensions.DependencyInjection.Extensions;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;

namespace verii_wms_api_v2.Modules.WarehouseAssistant;

public static class WarehouseAssistantModule
{
    public static IServiceCollection AddWarehouseAssistantModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<WarehouseAssistantOptions>()
            .Bind(configuration.GetSection(WarehouseAssistantOptions.SectionName))
            .Validate(options => options.TimeoutSeconds is >= 5 and <= 60,
                "WarehouseAssistant:TimeoutSeconds must be between 5 and 60 seconds.")
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps,
                "WarehouseAssistant:BaseUrl must be an absolute HTTPS URL.")
            .Validate(options => !options.EnableOpenAiIntentResolution || !string.IsNullOrWhiteSpace(options.Model),
                "WarehouseAssistant model is required when semantic intent resolution is enabled.")
            .Validate(options => options.MinimumSemanticConfidence is >= 0.50m and <= 0.95m,
                "WarehouseAssistant:MinimumSemanticConfidence must be between 0.50 and 0.95.")
            .Validate(options => !options.LocalEmbeddings.Enabled
                    || LocalWarehouseEmbeddingOptions.IsSafeLoopbackEndpoint(options.LocalEmbeddings.Endpoint),
                "WarehouseAssistant:LocalEmbeddings:Endpoint must be a loopback HTTP(S) URL without credentials, query or fragment.")
            .Validate(options => !options.LocalEmbeddings.Enabled
                    || !string.IsNullOrWhiteSpace(options.LocalEmbeddings.Model),
                "WarehouseAssistant:LocalEmbeddings:Model is required when local embeddings are enabled.")
            .Validate(options => options.LocalEmbeddings.TimeoutMilliseconds is >= 250 and <= 15000,
                "WarehouseAssistant:LocalEmbeddings:TimeoutMilliseconds must be between 250 and 15000.")
            .Validate(options => options.LocalEmbeddings.FailureBackoffSeconds is >= 5 and <= 300,
                "WarehouseAssistant:LocalEmbeddings:FailureBackoffSeconds must be between 5 and 300.")
            .Validate(options => options.LocalEmbeddings.MaximumBatchSize is >= 64 and <= 256,
                "WarehouseAssistant:LocalEmbeddings:MaximumBatchSize must be between 64 and 256.")
            .Validate(options => options.LocalEmbeddings.MaximumInputCharacters is >= 128 and <= 2000,
                "WarehouseAssistant:LocalEmbeddings:MaximumInputCharacters must be between 128 and 2000.")
            .Validate(options => (options.LocalEmbeddings.InputPrefix ?? string.Empty).Length <= 120,
                "WarehouseAssistant:LocalEmbeddings:InputPrefix cannot exceed 120 characters.")
            .Validate(options => options.LocalEmbeddings.MinimumSemanticSimilarity is >= -1m and <= 0.95m
                    && options.LocalEmbeddings.StrongSemanticSimilarity > options.LocalEmbeddings.MinimumSemanticSimilarity
                    && options.LocalEmbeddings.StrongSemanticSimilarity <= 1m,
                "WarehouseAssistant local semantic similarity thresholds are invalid.")
            .Validate(options => options.LocalEmbeddings.MinimumHybridConfidence is >= 0.50m and <= 0.95m
                    && options.LocalEmbeddings.AmbiguityMargin is >= 0.01m and <= 0.30m,
                "WarehouseAssistant local hybrid confidence settings are invalid.")
            .Validate(options => Math.Abs(
                    options.LocalEmbeddings.SemanticWeight
                    + options.LocalEmbeddings.RuleWeight
                    + options.LocalEmbeddings.EntityWeight
                    - 1m) <= 0.001m,
                "WarehouseAssistant local hybrid weights must total 1.0.")
            .ValidateOnStart();
        services.PostConfigure<WarehouseAssistantOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<WarehouseAssistantIntentResolver>();
        services.AddHttpClient(OllamaLocalWarehouseEmbeddingProvider.HttpClientName);
        services.AddSingleton<ILocalWarehouseEmbeddingProvider, OllamaLocalWarehouseEmbeddingProvider>();
        services.AddSingleton<ILocalWarehouseSemanticMatcher, LocalWarehouseSemanticMatcher>();
        services.AddHostedService<LocalWarehouseSemanticWarmupService>();
        services.AddScoped<LocalHybridWarehouseAssistantIntentResolver>();
        services.AddHttpClient<OpenAiWarehouseAssistantIntentResolver>();
        services.AddScoped<IWarehouseAssistantIntentResolver>(provider =>
            provider.GetRequiredService<OpenAiWarehouseAssistantIntentResolver>());
        services.AddScoped<IWarehouseAssistantRoutingDiagnostics>(provider =>
            provider.GetRequiredService<OpenAiWarehouseAssistantIntentResolver>());
        services.AddScoped<IWarehouseAssistantService, WarehouseAssistantService>();
        return services;
    }
}
