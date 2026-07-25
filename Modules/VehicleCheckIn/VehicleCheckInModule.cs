using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Modules.VehicleCheckIn.Infrastructure;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.VehicleCheckIn;

public static class VehicleCheckInModule
{
    public static IServiceCollection AddVehicleCheckInModule(this IServiceCollection services)=>services
        .AddPrivateUploadStorage()
        .AddScoped<IVehicleCheckInService,VehicleCheckInService>()
        .AddSingleton<IVehicleCheckInImageStorage,VehicleCheckInImageStorage>();
}
