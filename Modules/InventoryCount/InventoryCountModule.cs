using verii_wms_api_v2.Modules.InventoryCount.Application;

namespace verii_wms_api_v2.Modules.InventoryCount;

public static class InventoryCountModule
{
    public static IServiceCollection AddInventoryCountModule(this IServiceCollection services) =>
        services.AddScoped<IInventoryCountService, InventoryCountService>();
}
