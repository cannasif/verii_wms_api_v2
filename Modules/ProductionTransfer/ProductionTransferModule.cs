using verii_wms_api_v2.Modules.ProductionTransfer.Application;

namespace verii_wms_api_v2.Modules.ProductionTransfer;

public static class ProductionTransferModule
{
    public static IServiceCollection AddProductionTransferModule(this IServiceCollection services) =>
        services.AddScoped<IProductionTransferService,ProductionTransferService>()
            .AddScoped<IProductionTransferTaskService,ProductionTransferTaskService>();
}
