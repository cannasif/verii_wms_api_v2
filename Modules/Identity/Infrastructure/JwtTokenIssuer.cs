using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Domain;

namespace verii_wms_api_v2.Modules.Identity.Infrastructure;

public sealed class JwtTokenIssuer(IConfiguration configuration) : ITokenIssuer
{
    public AccessTokenResult CreateAccessToken(User user)
    {
        var secret = configuration["JwtSettings:SecretKey"] ?? throw new InvalidOperationException("JwtSettings:SecretKey is missing.");
        var expiresAt = DateTime.UtcNow.AddMinutes(configuration.GetValue("JwtSettings:AccessTokenMinutes", 15));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("tokenVersion", user.TokenVersion.ToString()),
            new Claim("firstName", user.Detail?.FirstName ?? string.Empty),
            new Claim("lastName", user.Detail?.LastName ?? string.Empty)
        };
        var token = new JwtSecurityToken(
            configuration["JwtSettings:Issuer"],
            configuration["JwtSettings:Audience"],
            claims,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));
        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
