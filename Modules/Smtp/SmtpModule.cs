using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Smtp.Application;
using verii_wms_api_v2.Modules.Smtp.Infrastructure;

namespace verii_wms_api_v2.Modules.Smtp;

public static class SmtpModule
{
    public static IServiceCollection AddSmtpModule(this IServiceCollection services)
    {
        services.AddScoped<SmtpSettingsService>();
        services.AddScoped<ISmtpSettingsService>(provider => provider.GetRequiredService<SmtpSettingsService>());
        services.AddScoped<IIdentityEmailSender>(provider => provider.GetRequiredService<SmtpSettingsService>());
        return services;
    }
}
