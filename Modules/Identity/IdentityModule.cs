using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

namespace verii_wms_api_v2.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services) => services
        .AddMemoryCache()
        .AddSingleton<IIdentitySessionValidator, IdentitySessionValidator>()
        .AddScoped<IIdentityService, IdentityService>()
        .AddScoped<IIdentitySessionMaintenance, IdentitySessionMaintenance>()
        .AddScoped<IPasswordPolicyService, PasswordPolicyService>()
        .AddScoped<IUserProfileService, UserProfileService>()
        .AddSingleton<ITokenIssuer, JwtTokenIssuer>()
        .AddSingleton<IProfileImageStorage, ProfileImageStorage>();
}
