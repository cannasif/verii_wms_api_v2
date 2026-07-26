using verii_wms_api_v2.Modules.Production.Application;

namespace verii_wms_api_v2.Modules.Production;

public static class ProductionModule
{
    public static IServiceCollection AddProductionModule(this IServiceCollection services) =>
        services.AddScoped<IProductionService,ProductionService>();
}
