using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.Stock.Infrastructure;

namespace verii_wms_api_v2.Modules.Stock;

public static class StockModule
{
    public static IServiceCollection AddStockModule(this IServiceCollection services)=>services
        .AddScoped<IStockImageService,StockImageService>()
        .AddSingleton<IStockImageStorage,StockImageStorage>();
}
