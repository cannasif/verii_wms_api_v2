using verii_wms_api_v2.Modules.Procurement.Application;

namespace verii_wms_api_v2.Modules.Procurement;

public static class ProcurementModule
{
    public static IServiceCollection AddProcurementModule(this IServiceCollection services)=>services
        .AddScoped<IProcurementPolicyService,ProcurementPolicyService>()
        .AddScoped<IProcurementService,ProcurementService>();
}
