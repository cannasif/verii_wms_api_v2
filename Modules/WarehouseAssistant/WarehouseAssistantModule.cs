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
            .Validate(options => !string.IsNullOrWhiteSpace(options.Version),
                "WarehouseAssistant:Version is required.")
            .Validate(options => options.MaximumMessageCharacters is >= 200 and <= 4_000,
                "WarehouseAssistant:MaximumMessageCharacters must be between 200 and 4000.")
            .Validate(options => options.MaximumQueriesPerMessage is >= 1 and <= 3,
                "WarehouseAssistant:MaximumQueriesPerMessage must be between 1 and 3.")
            .Validate(options => options.MaximumConversationSegments is >= 2 and <= 10,
                "WarehouseAssistant:MaximumConversationSegments must be between 2 and 10.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<WarehouseAssistantIntentResolver>();
        services.AddScoped<LocalHybridWarehouseAssistantIntentResolver>();
        services.AddScoped<IWarehouseAssistantIntentResolver>(provider =>
            provider.GetRequiredService<LocalHybridWarehouseAssistantIntentResolver>());
        services.AddScoped<IWarehouseAssistantRoutingDiagnostics>(provider =>
            provider.GetRequiredService<LocalHybridWarehouseAssistantIntentResolver>());
        services.AddScoped<IWarehouseAssistantService, WarehouseAssistantService>();
        return services;
    }
}
