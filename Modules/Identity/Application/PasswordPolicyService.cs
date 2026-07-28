using verii_wms_api_v2.Modules.ProjectSettings.Application;

namespace verii_wms_api_v2.Modules.Identity.Application;

public sealed record PasswordPolicyResponse(int MinimumLength, int MaximumLength);

public interface IPasswordPolicyService
{
    Task<PasswordPolicyResponse> GetAsync(CancellationToken cancellationToken = default);
    Task ValidateAsync(string? password, CancellationToken cancellationToken = default);
}

public sealed class PasswordPolicyService(IProjectSettingsService projectSettings) : IPasswordPolicyService
{
    public async Task<PasswordPolicyResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await projectSettings.GetAsync(cancellationToken);
        return new(settings.PasswordMinimumLength, IdentitySecurity.MaximumPasswordLength);
    }

    public async Task ValidateAsync(string? password, CancellationToken cancellationToken = default)
    {
        var policy = await GetAsync(cancellationToken);
        IdentitySecurity.ValidatePassword(password, policy.MinimumLength);
    }
}
