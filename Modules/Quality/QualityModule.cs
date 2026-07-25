using verii_wms_api_v2.Modules.Quality.Application;
namespace verii_wms_api_v2.Modules.Quality;
public static class QualityModule
{
    public static IServiceCollection AddQualityModule(this IServiceCollection services)=>services.AddScoped<QualityService>().AddScoped<IQualityService>(x=>x.GetRequiredService<QualityService>()).AddScoped<IQualityPolicyResolver>(x=>x.GetRequiredService<QualityService>());
}
