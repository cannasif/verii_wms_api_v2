using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;

namespace verii_wms_api_v2.Modules.Kkd;

public static class KkdModule
{
    public static IServiceCollection AddKkdModule(this IServiceCollection services) => services
        .AddScoped<IKkdDefinitionService, KkdDefinitionService>()
        .AddScoped<IKkdPolicyService, KkdPolicyService>()
        .AddScoped<IKkdEntitlementService, KkdEntitlementService>()
        .AddScoped<IKkdRequestService, KkdRequestService>()
        .AddScoped<IKkdPreparationTaskService, KkdPreparationTaskService>()
        .AddScoped<IKkdPreparationScanPickService, KkdPreparationScanPickService>()
        .AddScoped<IKkdReportService, KkdReportService>()
        .AddScoped<IKkdDistributionCompletionService, KkdDistributionCompletionService>()
        .AddScoped<IKkdDistributionService, KkdDistributionService>()
        .AddScoped<IWarehouseOutboundShipmentFinalizationHandler, KkdWarehouseOutboundShipmentFinalizationHandler>();
}
