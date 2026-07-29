using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Identity.Api;

[ApiController, Route("api/auth"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(
    IIdentityService identityService,
    IPasswordPolicyService passwordPolicy,
    IWebHostEnvironment environment,
    ILogger<AuthController> logger) : ControllerBase
{
    private string RefreshCookieName => environment.IsDevelopment() ? "wms.refresh.dev" : "__Host-wms-refresh";

    [AllowAnonymous, EnableRateLimiting("identity-sensitive"), HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var session = await identityService.LoginAsync(request, CurrentClient(), cancellationToken);
        SetRefreshCookie(session);
        return Ok(ApiResponse<AuthTokenResponse>.Ok(session.Response, "Giriş başarılı."));
    }

    [AllowAnonymous, HttpGet("password-policy")]
    public async Task<IActionResult> PasswordPolicy(CancellationToken cancellationToken) =>
        Ok(ApiResponse<PasswordPolicyResponse>.Ok(await passwordPolicy.GetAsync(cancellationToken)));

    [AllowAnonymous, EnableRateLimiting("identity-refresh"), HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            var refreshToken = Request.Cookies[RefreshCookieName] ?? string.Empty;
            var session = await identityService.RefreshAsync(refreshToken, CurrentClient(), cancellationToken);
            SetRefreshCookie(session);
            return Ok(ApiResponse<AuthTokenResponse>.Ok(session.Response));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AppException exception) when (exception.StatusCode is
            StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            DeleteRefreshCookies();
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Refresh token processing failed. TraceId={TraceId}",
                HttpContext.TraceIdentifier);
            DeleteRefreshCookies();
            throw AppException.Unauthorized("Oturum yenilenemedi. Lütfen yeniden giriş yapın.");
        }
    }

    [AllowAnonymous, HttpPost("revoke")]
    public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
    {
        try
        {
            var refreshToken = Request.Cookies[RefreshCookieName] ?? string.Empty;
            await identityService.RevokeAsync(refreshToken, CurrentClient(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Logout is idempotent from the browser's perspective. A stale or corrupt server-side
            // session must never prevent the HttpOnly cookie from being cleared.
            logger.LogWarning(
                exception,
                "Refresh token revocation could not be persisted. The client cookie will still be cleared. TraceId={TraceId}",
                HttpContext.TraceIdentifier);
        }
        finally
        {
            DeleteRefreshCookies();
        }

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
        var session = await identityService.ChangePasswordAsync(
            CurrentUserId(),
            CurrentBranchCode(),
            request,
            CurrentClient(),
            cancellationToken);
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

    private string CurrentBranchCode() =>
        User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)
        ?? throw AppException.Unauthorized("Oturum şube bilgisi geçersiz.");
}
