using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Identity.Application;

public static class IdentitySecurity
{
    public const int MinimumConfigurablePasswordLength = 5;
    public const int DefaultMinimumPasswordLength = 6;
    public const int MaximumPasswordLength = 15;

    public static string CreateOpaqueToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public static string HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public static void ValidatePassword(string? password, int minimumLength)
    {
        if (minimumLength is < MinimumConfigurablePasswordLength or > MaximumPasswordLength)
            throw new InvalidOperationException("Geçersiz şifre politikası yapılandırması.");
        if (password is null || password.Length < minimumLength || password.Length > MaximumPasswordLength)
            throw AppException.BadRequest($"Şifre {minimumLength}-{MaximumPasswordLength} karakter arasında olmalıdır.");
    }
}
