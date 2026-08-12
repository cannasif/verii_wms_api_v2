using verii_wms_api_v2.Modules.GeneratorProduction.Application;

namespace verii_wms_api_v2.Modules.GeneratorProduction;

public static class GeneratorProductionModule
{
    public static IServiceCollection AddGeneratorProductionModule(this IServiceCollection services) =>
        services.AddScoped<IGeneratorProductionService, GeneratorProductionService>();
}
