using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

namespace verii_wms_api_v2.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services) => services
        .AddScoped<IIdentityService, IdentityService>()
        .AddScoped<IUserProfileService, UserProfileService>()
        .AddSingleton<ITokenIssuer, JwtTokenIssuer>()
        .AddSingleton<IProfileImageStorage, ProfileImageStorage>();
}
