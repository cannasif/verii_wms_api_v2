using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
namespace verii_wms_api_v2.Modules.BarcodeDesigner;
public static class BarcodeDesignerModule
{
    public static IServiceCollection AddBarcodeDesignerModule(this IServiceCollection services) => services
        .AddScoped<IBarcodeDesignerService, BarcodeDesignerService>()
        .AddScoped<IBarcodePolicyService, BarcodePolicyService>()
        .AddScoped<IWarehouseBarcodeResolver, WarehouseBarcodeResolutionService>();
}
