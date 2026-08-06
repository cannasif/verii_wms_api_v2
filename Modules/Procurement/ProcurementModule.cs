using verii_wms_api_v2.Modules.Procurement.Application;
using verii_wms_api_v2.Modules.Procurement.Infrastructure;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.Procurement;

public static class ProcurementModule
{
    public static IServiceCollection AddProcurementModule(this IServiceCollection services)=>services
        .AddPrivateUploadStorage()
        .AddSingleton<IProcurementAttachmentStorage,ProcurementAttachmentStorage>()
        .AddScoped<IProcurementPolicyService,ProcurementPolicyService>()
        .AddScoped<IProcurementSupplierPortalService,ProcurementSupplierPortalService>()
        .AddScoped<IProcurementService,ProcurementService>();
}
