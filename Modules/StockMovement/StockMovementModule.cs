using verii_wms_api_v2.Modules.StockMovement.Application;

namespace verii_wms_api_v2.Modules.StockMovement;

public static class StockMovementModule
{
    public static IServiceCollection AddStockMovementModule(this IServiceCollection services) =>
        services.AddScoped<IStockMovementService, StockMovementService>();
}
