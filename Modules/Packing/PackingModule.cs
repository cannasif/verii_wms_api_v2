using verii_wms_api_v2.Modules.Packing.Application;
namespace verii_wms_api_v2.Modules.Packing;
public static class PackingModule
{
    public static IServiceCollection AddPackingModule(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(HttpPackingDeviceGateway),client=>client.Timeout=TimeSpan.FromSeconds(15));
        services.AddScoped<IPackingService,PackingService>();
        services.AddScoped<PackingSourceAdapterResolver>();
        services.AddScoped<IPackingSourceAdapter,WarehouseOutboundPackingSourceAdapter>();
        services.AddScoped<IPackingSourceAdapter,ShipmentPackingSourceAdapter>();
        services.AddScoped<IPackingSourceAdapter,WarehouseTransferPackingSourceAdapter>();
        services.AddScoped<IPackingDeviceGateway,HttpPackingDeviceGateway>();
        services.AddScoped<IPackingDeviceService,PackingDeviceService>();
        services.AddScoped<IPackingPrintQueueJobRunner,PackingPrintQueueJobRunner>();
        return services;
    }
}
