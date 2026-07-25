using verii_wms_api_v2.Modules.SystemManagement.Application;
using verii_wms_api_v2.Modules.SystemManagement.Application.Users;
using verii_wms_api_v2.Modules.SystemManagement.Infrastructure;

namespace verii_wms_api_v2.Modules.SystemManagement;

public static class SystemManagementModule
{
    public static IServiceCollection AddSystemManagementModule(this IServiceCollection services) => services
        .AddScoped<IUserManagementService, UserManagementService>()
        .AddScoped<IHangfireExecutionLogStore, HangfireExecutionLogStore>()
        .AddScoped<IHangfireExecutionLogService, HangfireExecutionLogService>()
        .AddScoped<ITrackedErpMirrorJobRunner, TrackedErpMirrorJobRunner>();
}
