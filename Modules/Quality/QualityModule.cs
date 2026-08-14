using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Shared.Infrastructure.Files;
namespace verii_wms_api_v2.Modules.Quality;
public static class QualityModule
{
    public static IServiceCollection AddQualityModule(this IServiceCollection services)=>services
        .AddPrivateUploadStorage()
        .AddScoped<QualityService>()
        .AddScoped<IQualityService>(x=>x.GetRequiredService<QualityService>())
        .AddScoped<IQualityPolicyResolver>(x=>x.GetRequiredService<QualityService>())
        .AddScoped<IQualityWarehouseRoutingResolver>(x=>x.GetRequiredService<QualityService>())
        .AddScoped<IQualityRuleImportService,QualityRuleImportService>()
        .AddScoped<IQualityInspectionImageService,QualityInspectionImageService>()
        .AddScoped<IGoodsReceiptErpSuccessJob,QualityDispositionDatJob>();
}
