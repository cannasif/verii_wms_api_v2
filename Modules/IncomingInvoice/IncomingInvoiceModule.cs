using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Modules.IncomingInvoice.Infrastructure;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.IncomingInvoice;

public static class IncomingInvoiceModule
{
    public static IServiceCollection AddIncomingInvoiceModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ELogoPostboxOptions>(
            configuration.GetSection(ELogoPostboxOptions.SectionName));
        services.AddPrivateUploadStorage();
        services.AddScoped<IELogoConnectionService, ELogoConnectionService>();
        services.AddScoped<IIncomingInvoiceService, IncomingInvoiceService>();
        services.AddSingleton<IIncomingInvoiceDocumentStorage, IncomingInvoiceDocumentStorage>();
        services.AddHttpClient<IELogoPostboxClient, ELogoPostboxClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("V3RII-WMS/2.0");
        });
        return services;
    }
}
