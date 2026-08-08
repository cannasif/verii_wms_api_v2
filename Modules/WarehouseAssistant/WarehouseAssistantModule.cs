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
            .Bind(configuration.GetSection(WarehouseAssistantOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<WarehouseAssistantIntentResolver>();
        services.AddHttpClient<OpenAiWarehouseAssistantIntentResolver>();
        services.AddScoped<IWarehouseAssistantIntentResolver>(provider =>
            provider.GetRequiredService<OpenAiWarehouseAssistantIntentResolver>());
        services.AddScoped<IWarehouseAssistantService, WarehouseAssistantService>();
        return services;
    }
}
