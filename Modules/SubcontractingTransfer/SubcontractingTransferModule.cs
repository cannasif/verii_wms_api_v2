using verii_wms_api_v2.Modules.SubcontractingTransfer.Application;

namespace verii_wms_api_v2.Modules.SubcontractingTransfer;

public static class SubcontractingTransferModule
{
    public static IServiceCollection AddSubcontractingTransferModule(this IServiceCollection services)=>
        services.AddScoped<ISubcontractingTransferService,SubcontractingTransferService>();
}
