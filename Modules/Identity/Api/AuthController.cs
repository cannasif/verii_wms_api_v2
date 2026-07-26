using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Identity.Api;

[ApiController, Route("api/auth"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(IIdentityService identityService, IWebHostEnvironment environment) : ControllerBase
{
    private string RefreshCookieName => environment.IsDevelopment() ? "wms.refresh.dev" : "__Host-wms-refresh";

    [AllowAnonymous, EnableRateLimiting("identity-sensitive"), HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var session = await identityService.LoginAsync(request, CurrentClient(), cancellationToken);
        SetRefreshCookie(session);
        return Ok(ApiResponse<AuthTokenResponse>.Ok(session.Response, "Giriş başarılı."));
    }

    [AllowAnonymous, EnableRateLimiting("identity-refresh"), HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshCookieName] ?? string.Empty;
        var session = await identityService.RefreshAsync(refreshToken, CurrentClient(), cancellationToken);
        SetRefreshCookie(session);
        return Ok(ApiResponse<AuthTokenResponse>.Ok(session.Response));
    }

    [AllowAnonymous, HttpPost("revoke")]
    public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshCookieName] ?? string.Empty;
        await identityService.RevokeAsync(refreshToken, CurrentClient(), cancellationToken);
        DeleteRefreshCookies();
        return Ok(ApiResponse<string>.Ok(string.Empty, "Oturum kapatıldı."));
    }

    [AllowAnonymous, EnableRateLimiting("identity-sensitive"), HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await identityService.ForgotPasswordAsync(request, CurrentClient(), cancellationToken);
        return Ok(ApiResponse<string>.Ok(string.Empty, "E-posta adresi kayıtlıysa şifre yenileme bağlantısı gönderilecektir."));
    }

    [AllowAnonymous, EnableRateLimiting("identity-sensitive"), HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await identityService.ResetPasswordAsync(request, cancellationToken);
        DeleteRefreshCookies();
        return Ok(ApiResponse<string>.Ok(string.Empty, "Şifreniz yenilendi. Yeniden giriş yapabilirsiniz."));
    }

    [Authorize, HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var session = await identityService.ChangePasswordAsync(CurrentUserId(), request, CurrentClient(), cancellationToken);
        SetRefreshCookie(session);
        return Ok(ApiResponse<AuthTokenResponse>.Ok(session.Response, "Şifre güncellendi."));
    }

    private ClientContext CurrentClient() => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString());

    private void SetRefreshCookie(AuthSessionResult session) => Response.Cookies.Append(
        RefreshCookieName,
        session.RefreshToken,
        new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            Expires = session.RefreshTokenExpiresAt
        });

    private void DeleteRefreshCookies()
    {
        DeleteCookie("wms.refresh.dev", secure: false);
        DeleteCookie("__Host-wms-refresh", secure: true);
    }

    private void DeleteCookie(string name, bool secure) => Response.Cookies.Delete(name, new CookieOptions
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true
    });

    private long CurrentUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
