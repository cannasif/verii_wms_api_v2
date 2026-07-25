using verii_wms_api_v2.Modules.WarehouseOutbound.Application;

namespace verii_wms_api_v2.Modules.WarehouseOutbound;

public static class WarehouseOutboundModule{
 public static IServiceCollection AddWarehouseOutboundModule(this IServiceCollection services)=>services
  .AddScoped<IWarehouseOutboundService,WarehouseOutboundService>()
  .AddScoped<IWarehouseOutboundOperationService,WarehouseOutboundOperationService>()
  .AddScoped<IWarehouseOutboundReservationService,WarehouseOutboundReservationService>()
  .AddScoped<IWarehouseOutboundPolicyService,WarehouseOutboundPolicyService>();
}
