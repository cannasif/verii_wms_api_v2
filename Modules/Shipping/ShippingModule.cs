using verii_wms_api_v2.Modules.Shipping.Application;

namespace verii_wms_api_v2.Modules.Shipping;

public static class ShippingModule{
 public static IServiceCollection AddShippingModule(this IServiceCollection services)=>services
  .AddScoped<IShippingService,ShippingService>()
  .AddScoped<IShippingOperationService,ShippingOperationService>()
  .AddScoped<IShipmentReservationService,ShipmentReservationService>()
  .AddScoped<IShipmentPolicyService,ShipmentPolicyService>();
}
