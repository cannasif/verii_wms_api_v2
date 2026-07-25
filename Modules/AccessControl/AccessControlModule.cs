using verii_wms_api_v2.Modules.AccessControl.Application;

namespace verii_wms_api_v2.Modules.AccessControl;

public static class AccessControlModule
{
    public static IServiceCollection AddAccessControlModule(this IServiceCollection services) => services
        .AddScoped<IPermissionAuthorizationService, PermissionAuthorizationService>()
        .AddScoped<IAccessControlService, AccessControlService>();
}
