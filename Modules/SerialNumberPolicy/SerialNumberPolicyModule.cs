using verii_wms_api_v2.Modules.SerialNumberPolicy.Application;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Infrastructure;
namespace verii_wms_api_v2.Modules.SerialNumberPolicy;
public static class SerialNumberPolicyModule
{
    public static IServiceCollection AddSerialNumberPolicyModule(this IServiceCollection services)=>services
        .AddScoped<ISerialSequenceAllocator,SqlServerSerialSequenceAllocator>()
        .AddScoped<SerialNumberPolicyService>()
        .AddScoped<ISerialNumberPolicyService>(x=>x.GetRequiredService<SerialNumberPolicyService>())
        .AddScoped<ISerialNumberPolicyResolver>(x=>x.GetRequiredService<SerialNumberPolicyService>());
}
