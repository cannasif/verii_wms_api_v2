using verii_wms_api_v2.Modules.ErpBalanceSync.Application;
using verii_wms_api_v2.Modules.ErpBalanceSync.Infrastructure;

namespace verii_wms_api_v2.Modules.ErpBalanceSync;

public static class ErpBalanceSyncModule
{
    public static IServiceCollection AddErpBalanceSyncModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ErpStockBalanceSyncOptions>()
            .Bind(configuration.GetSection(ErpStockBalanceSyncOptions.SectionName))
            .Validate(x => x.BatchSize is >= 50 and <= 5000, "ErpBalanceSync:BatchSize must be between 50 and 5000.")
            .Validate(x => x.CommandTimeoutSeconds is >= 30 and <= 900, "ErpBalanceSync:CommandTimeoutSeconds must be between 30 and 900.")
            .Validate(x => x.MinimumPreviousSourceRatio is >= 0 and <= 1, "ErpBalanceSync:MinimumPreviousSourceRatio must be between 0 and 1.")
            .ValidateOnStart();
        return services
            .AddScoped<IErpStockBalanceSyncStore, SqlServerErpStockBalanceSyncStore>()
            .AddScoped<IErpStockBalanceQueryService, ErpStockBalanceQueryService>()
            .AddScoped<IErpStockBalanceSyncJobRunner, ErpStockBalanceSyncJobRunner>();
    }
}
