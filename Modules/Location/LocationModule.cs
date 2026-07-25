using verii_wms_api_v2.Modules.Location.Application;

namespace verii_wms_api_v2.Modules.Location;

public static class LocationModule
{
    public static IServiceCollection AddLocationModule(this IServiceCollection services) =>
        services.AddScoped<ILocationService, LocationService>();
}
