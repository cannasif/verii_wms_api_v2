using Microsoft.Extensions.Options;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

/// <summary>
/// Initializes the local model and immutable intent catalog in the background so API startup
/// is never blocked and the first assistant question does not pay the cold-start cost.
/// </summary>
internal sealed class LocalWarehouseSemanticWarmupService(
    ILocalWarehouseSemanticMatcher matcher,
    ILocalWarehouseEmbeddingProvider embeddingProvider,
    IOptions<WarehouseAssistantOptions> options,
    ILogger<LocalWarehouseSemanticWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.LocalEmbeddings.WarmOnStartup || !matcher.IsConfigured)
            return;

        try
        {
            await embeddingProvider.EmbedAsync(["Depo asistanı yerel dil motorunu hazırlıyor."], stoppingToken);
            var result = await matcher.MatchAsync("Depo asistanı vardiya özetini hazırlasın.", stoppingToken);
            if (result.IsAvailable)
                logger.LogInformation("Local warehouse semantic engine warmed with model {Model}.", result.Model);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Local warehouse semantic warm-up failed; deterministic routing remains active.");
        }
    }
}
