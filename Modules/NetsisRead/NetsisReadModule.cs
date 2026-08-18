using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Infrastructure;

namespace verii_wms_api_v2.Modules.NetsisRead;
public static class NetsisReadModule
{
    public static IServiceCollection AddNetsisReadModule(this IServiceCollection services) => services
        .AddScoped<INetsisQueryExecutor,NetsisQueryExecutor>()
        .AddScoped<INetsisReadService,NetsisReadService>()
        .AddScoped<INetsisImportOpenFileReader>(provider => provider.GetRequiredService<INetsisReadService>());
}
