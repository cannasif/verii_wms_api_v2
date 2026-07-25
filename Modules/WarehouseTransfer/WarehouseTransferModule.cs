using verii_wms_api_v2.Modules.WarehouseTransfer.Application;

namespace verii_wms_api_v2.Modules.WarehouseTransfer;

public static class WarehouseTransferModule
{
    public static IServiceCollection AddWarehouseTransferModule(this IServiceCollection services)=>
        services.AddScoped<IWarehouseTransferService,WarehouseTransferService>()
            .AddScoped<IWarehouseTransferOperationService,WarehouseTransferOperationService>()
            .AddScoped<IWarehouseTransferReservationService,WarehouseTransferReservationService>()
            .AddScoped<IWarehouseTransferPolicyService,WarehouseTransferPolicyService>();
}
