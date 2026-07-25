using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Infrastructure;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.SteelReceipt;

public static class SteelReceiptModule
{
    public static IServiceCollection AddSteelReceiptModule(this IServiceCollection services)=>services
        .AddPrivateUploadStorage()
        .AddScoped<ISteelReceiptService,SteelReceiptService>()
        .AddSingleton<ISteelReceiptAttachmentStorage,SteelReceiptAttachmentStorage>();
}
