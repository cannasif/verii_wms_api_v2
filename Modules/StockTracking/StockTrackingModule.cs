using verii_wms_api_v2.Modules.StockTracking.Application;

namespace verii_wms_api_v2.Modules.StockTracking;

public static class StockTrackingModule
{
    public static IServiceCollection AddStockTrackingModule(this IServiceCollection services) => services
        .AddScoped<StockTrackingPolicyService>()
        .AddScoped<IStockTrackingPolicyService>(x => x.GetRequiredService<StockTrackingPolicyService>())
        .AddScoped<IStockTrackingPolicyResolver>(x => x.GetRequiredService<StockTrackingPolicyService>());
}
