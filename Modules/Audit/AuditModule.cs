using verii_wms_api_v2.Modules.Audit.Application;

namespace verii_wms_api_v2.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services) => services
        .AddScoped<IAuditLogWriter, AuditLogWriter>()
        .AddScoped<IAuditLogQueryService, AuditLogQueryService>();
}
