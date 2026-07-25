using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Infrastructure;

namespace verii_wms_api_v2.Modules.DocumentSeries;

public static class DocumentSeriesModule
{
    public static IServiceCollection AddDocumentSeriesModule(this IServiceCollection services) => services
        .AddScoped<IDocumentSeriesService, DocumentSeriesService>()
        .AddScoped<IDocumentNumberAllocator, SqlServerDocumentNumberAllocator>();
}
