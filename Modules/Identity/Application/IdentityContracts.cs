using verii_wms_api_v2.Modules.Identity.Domain;

namespace verii_wms_api_v2.Modules.Identity.Application;

public sealed record LoginRequest
{
    public string? Identifier { get; init; }
    public string? Email { get; init; } // Legacy client compatibility.
    public string Password { get; init; } = string.Empty;
    public string BranchCode { get; init; } = string.Empty;

    public string ResolveIdentifier() =>
        !string.IsNullOrWhiteSpace(Identifier) ? Identifier : Email ?? string.Empty;
}
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ClientContext(string? IpAddress, string? UserAgent);
public sealed record AccessTokenResult(string Value, DateTime ExpiresAt);
public sealed record AuthTokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string BranchCode);
public sealed record AuthSessionResult(AuthTokenResponse Response, string RefreshToken, DateTime RefreshTokenExpiresAt);

public sealed record ProfileRequest(decimal? Height, decimal? Weight, string? Description, int? Gender);
public sealed record UserAppearanceRequest(bool BackgroundMotionEnabled, string BackgroundMotionVariant);
public sealed record UserProfileResponse(
    long Id,
    long UserId,
    string? ProfilePictureUrl,
    decimal? Height,
    decimal? Weight,
    string? Description,
    int? Gender,
    bool BackgroundMotionEnabled,
    string BackgroundMotionVariant,
    DateTime? CreatedDate,
    DateTime? UpdatedDate);
public sealed record ProfileImageUpload(Stream Content, string FileName, string? ContentType, long Length);

public interface IIdentityService
{
    Task<AuthSessionResult> LoginAsync(LoginRequest request, ClientContext client, CancellationToken cancellationToken = default);
    Task<AuthSessionResult> RefreshAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken = default);
    Task RevokeAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, ClientContext client, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<AuthSessionResult> ChangePasswordAsync(long userId, string branchCode, ChangePasswordRequest request, ClientContext client, CancellationToken cancellationToken = default);
}

public interface ITokenIssuer { AccessTokenResult CreateAccessToken(User user, string branchCode); }

public interface IIdentityEmailSender
{
    Task SendPasswordResetAsync(string recipientEmail, string resetUrl, CancellationToken cancellationToken = default);
}

public interface IUserProfileService
{
    Task<UserProfileResponse> GetCurrentAsync(long userId, CancellationToken cancellationToken = default);
    Task<UserProfileResponse> UpsertAsync(long userId, string firstName, string lastName, ProfileRequest request, CancellationToken cancellationToken = default);
    Task<UserProfileResponse> UpdateAppearanceAsync(long userId, string firstName, string lastName, UserAppearanceRequest request, CancellationToken cancellationToken = default);
    Task<string> UploadPictureAsync(long userId, string firstName, string lastName, ProfileImageUpload upload, CancellationToken cancellationToken = default);
    Task DeletePictureAsync(long userId, CancellationToken cancellationToken = default);
}

public interface IProfileImageStorage
{
    Task<string> SaveAsync(long userId, ProfileImageUpload upload, CancellationToken cancellationToken = default);
    Task DeleteIfManagedAsync(string? relativeUrl, CancellationToken cancellationToken = default);
}
