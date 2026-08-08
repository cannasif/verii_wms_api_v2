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
            .Validate(options => !options.EnableOpenAiIntentResolution
                    || (!string.IsNullOrWhiteSpace(options.Model) && !string.IsNullOrWhiteSpace(options.ApiKey)),
                "WarehouseAssistant model and API key are required when OpenAI intent resolution is enabled.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<WarehouseAssistantIntentResolver>();
        services.AddHttpClient<OpenAiWarehouseAssistantIntentResolver>();
        services.AddScoped<IWarehouseAssistantIntentResolver>(provider =>
            provider.GetRequiredService<OpenAiWarehouseAssistantIntentResolver>());
        services.AddScoped<IWarehouseAssistantService, WarehouseAssistantService>();
        return services;
    }
}
