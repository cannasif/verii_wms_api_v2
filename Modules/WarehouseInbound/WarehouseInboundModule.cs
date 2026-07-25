using verii_wms_api_v2.Modules.WarehouseInbound.Application;
using verii_wms_api_v2.Modules.WarehouseInbound.Infrastructure;

namespace verii_wms_api_v2.Modules.WarehouseInbound;

public static class WarehouseInboundModule
{
    public static IServiceCollection AddWarehouseInboundModule(this IServiceCollection services) => services
        .AddScoped<IWarehouseInboundService, WarehouseInboundService>()
        .AddScoped<IWarehouseInboundOperationsService, WarehouseInboundOperationsService>()
        .AddScoped<IWarehouseInboundTaskService, WarehouseInboundTaskService>()
        .AddScoped<IWarehouseInboundLabelService, WarehouseInboundLabelService>()
        .AddScoped<IWarehouseInboundExecutionService, WarehouseInboundExecutionService>()
        .AddScoped<IWarehouseInboundLifecycleService, WarehouseInboundLifecycleService>()
        .AddScoped<IWarehouseInboundPolicyService, WarehouseInboundPolicyService>()
        .AddScoped<IWarehouseInboundOrderSource, SqlWarehouseInboundOrderSource>();
}
