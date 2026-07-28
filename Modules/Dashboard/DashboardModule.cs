using verii_wms_api_v2.Modules.Dashboard.Application;

namespace verii_wms_api_v2.Modules.Dashboard;

public static class DashboardModule
{
    public static IServiceCollection AddDashboardModule(this IServiceCollection services) =>
        services.AddScoped<IDashboardService, DashboardService>();
}
