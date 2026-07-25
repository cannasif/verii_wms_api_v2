using verii_wms_api_v2.Modules.ProjectSettings.Application;

namespace verii_wms_api_v2.Modules.ProjectSettings;

public static class ProjectSettingsModule
{
    public static IServiceCollection AddProjectSettingsModule(this IServiceCollection services) =>
        services.AddMemoryCache().AddScoped<IProjectSettingsService, ProjectSettingsService>();
}
