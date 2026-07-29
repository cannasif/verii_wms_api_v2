using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Identity.Application;

public sealed class IdentityService(
    IUnitOfWork unitOfWork,
    ITokenIssuer tokenIssuer,
    IIdentitySessionValidator sessionValidator,
    IPasswordPolicyService passwordPolicy,
    IIdentityEmailSender emailSender,
    IConfiguration configuration,
    ILogger<IdentityService> logger) : IIdentityService
{
    private IGenericRepository<User> Users => unitOfWork.Repository<User>();
    private IGenericRepository<RefreshTokenSession> RefreshTokens => unitOfWork.Repository<RefreshTokenSession>();
    private IGenericRepository<PasswordResetToken> ResetTokens => unitOfWork.Repository<PasswordResetToken>();

    public async Task<AuthSessionResult> LoginAsync(LoginRequest request, ClientContext client, CancellationToken cancellationToken = default)
    {
        var value = request.ResolveIdentifier().Trim().ToLowerInvariant();
        var branchCode = NormalizeBranchCode(request.BranchCode);
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(request.Password))
            throw AppException.Unauthorized("Kullanıcı adı veya şifre hatalı.");

        var user = await Users.Query(tracking: true).Include(x => x.Detail)
            .FirstOrDefaultAsync(x => x.IsActive && (x.Email.ToLower() == value || x.Username.ToLower() == value), cancellationToken);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw AppException.Unauthorized("Kullanıcı adı veya şifre hatalı.");

        user.LastLoginAt = DateTime.UtcNow;
        var session = await CreateSessionAsync(user, branchCode, Guid.NewGuid(), client, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToAuthSession(user, session);
    }

    public async Task<AuthSessionResult> RefreshAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw AppException.Unauthorized("Oturum yenilenemedi.");

        var tokenHash = IdentitySecurity.HashToken(refreshToken);
        var outcome = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var current = await RefreshTokens.Query(tracking: true).Include(x => x.User).ThenInclude(x => x.Detail)
                .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
            if (current is null || !current.User.IsActive)
                return RefreshOutcome.Invalid();

            var now = DateTime.UtcNow;
            if (current.RevokedAt.HasValue)
            {
                var replayGraceSeconds = Math.Clamp(
                    configuration.GetValue("Identity:RefreshTokenReplayGraceSeconds", 15),
                    0,
                    60);
                if (RefreshTokenReplayPolicy.IsAllowed(
                        current,
                        client,
                        now,
                        TimeSpan.FromSeconds(replayGraceSeconds)))
                {
                    var concurrentReplacement = await CreateSessionAsync(
                        current.User,
                        current.FamilyId,
                        client,
                        ct,
                        current.ExpiresAt);
                    await unitOfWork.SaveChangesAsync(ct);
                    logger.LogInformation(
                        "Concurrent refresh replay accepted within grace window. UserId={UserId} FamilyId={FamilyId}",
                        current.UserId,
                        current.FamilyId);
                    return RefreshOutcome.Valid(ToAuthSession(current.User, concurrentReplacement));
                }

                await RevokeFamilyAsync(current.User, current.FamilyId, "ReuseDetected", client, now, ct);
                current.User.TokenVersion++;
                await unitOfWork.SaveChangesAsync(ct);
                sessionValidator.Invalidate(current.UserId);
                return RefreshOutcome.Invalid();
            }

            if (current.ExpiresAt <= now)
            {
                Revoke(current, "Expired", client, now);
                await unitOfWork.SaveChangesAsync(ct);
                return RefreshOutcome.Invalid();
            }

            var replacement = await CreateSessionAsync(
                current.User,
                current.FamilyId,
                client,
                ct,
                current.ExpiresAt);
            current.ReplacedByTokenHash = replacement.Entity.TokenHash;
            Revoke(current, "Rotated", client, now);
            await unitOfWork.SaveChangesAsync(ct);
            return RefreshOutcome.Valid(ToAuthSession(current.User, replacement));
        }, cancellationToken, IsolationLevel.Serializable);

        return outcome.Session ?? throw AppException.Unauthorized("Oturum yenilenemedi.");
    }

    public async Task RevokeAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var tokenHash = IdentitySecurity.HashToken(refreshToken);
        var session = await RefreshTokens.Query(tracking: true).Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (session is null) return;

        await RevokeFamilyAsync(
            session.User,
            session.FamilyId,
            "Logout",
            client,
            DateTime.UtcNow,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, ClientContext client, CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return;
        PasswordResetToken? issuedToken = null;

        try
        {
            var user = await Users.Query(tracking: true)
                .FirstOrDefaultAsync(x => x.IsActive && x.Email.ToLower() == email, cancellationToken);
            if (user is null) return;

            var now = DateTime.UtcNow;
            var activeTokens = await ResetTokens.Query(tracking: true)
                .Where(x => x.UserId == user.Id && x.ConsumedAt == null && x.ExpiresAt > now)
                .ToListAsync(cancellationToken);
            foreach (var token in activeTokens) token.ConsumedAt = now;

            var rawToken = IdentitySecurity.CreateOpaqueToken();
            issuedToken = new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = IdentitySecurity.HashToken(rawToken),
                ExpiresAt = now.AddMinutes(configuration.GetValue("Identity:PasswordResetTokenMinutes", 30)),
                RequestedByIp = Limit(client.IpAddress, 64)
            };
            await ResetTokens.AddAsync(issuedToken, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var baseUrl = configuration["FrontendSettings:BaseUrl"]?.TrimEnd('/')
                ?? throw new InvalidOperationException("FrontendSettings:BaseUrl is missing.");
            var path = configuration["FrontendSettings:ResetPasswordPath"] ?? "/auth/reset-password";
            await emailSender.SendPasswordResetAsync(user.Email, $"{baseUrl}{path}?token={Uri.EscapeDataString(rawToken)}", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await InvalidateUndeliveredResetTokenAsync(issuedToken);
            throw;
        }
        catch (Exception exception)
        {
            await InvalidateUndeliveredResetTokenAsync(issuedToken);
            logger.LogError(exception, "Password reset request could not be completed.");
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        await passwordPolicy.ValidateAsync(request.NewPassword, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Token)) throw InvalidResetToken();
        var tokenHash = IdentitySecurity.HashToken(request.Token);

        var resetUserId = await unitOfWork.ExecuteInTransactionAsync<long?>(async ct =>
        {
            var now = DateTime.UtcNow;
            var token = await ResetTokens.Query(tracking: true).Include(x => x.User)
                .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);
            if (token is null || token.ConsumedAt.HasValue || token.ExpiresAt <= now || !token.User.IsActive)
                return null;

            token.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            token.User.PasswordLength = request.NewPassword.Length;
            token.User.TokenVersion++;

            var userTokens = await ResetTokens.Query(tracking: true)
                .Where(x => x.UserId == token.UserId && x.ConsumedAt == null)
                .ToListAsync(ct);
            foreach (var item in userTokens) item.ConsumedAt = now;
            await RevokeAllSessionsAsync(token.UserId, "PasswordReset", null, now, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return token.UserId;
        }, cancellationToken, IsolationLevel.Serializable);

        if (!resetUserId.HasValue) throw InvalidResetToken();
        sessionValidator.Invalidate(resetUserId.Value);
    }

    public async Task<AuthSessionResult> ChangePasswordAsync(long userId, string branchCode, ChangePasswordRequest request, ClientContext client, CancellationToken cancellationToken = default)
    {
        await passwordPolicy.ValidateAsync(request.NewPassword, cancellationToken);
        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var user = await Users.Query(tracking: true).Include(x => x.Detail)
                .FirstOrDefaultAsync(x => x.Id == userId && x.IsActive, ct)
                ?? throw AppException.NotFound("Kullanıcı bulunamadı.");
            if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                throw AppException.BadRequest("Mevcut şifre hatalı.");

            var now = DateTime.UtcNow;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            user.PasswordLength = request.NewPassword.Length;
            user.TokenVersion++;
            await RevokeAllSessionsAsync(user.Id, "PasswordChanged", client, now, ct);
            var session = await CreateSessionAsync(user, NormalizeBranchCode(branchCode), Guid.NewGuid(), client, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return ToAuthSession(user, session);
        }, cancellationToken, IsolationLevel.Serializable);
        sessionValidator.Invalidate(userId);
        return result;
    }

    private async Task<IssuedRefreshSession> CreateSessionAsync(
        User user,
        string branchCode,
        Guid familyId,
        ClientContext client,
        CancellationToken cancellationToken,
        DateTime? absoluteExpiresAt = null)
    {
        var rawToken = IdentitySecurity.CreateOpaqueToken();
        var expiresAt = absoluteExpiresAt
            ?? DateTime.UtcNow.AddDays(configuration.GetValue("Identity:RefreshTokenDays", 30));
        var entity = new RefreshTokenSession
        {
            BranchCode = NormalizeBranchCode(branchCode),
            UserId = user.Id,
            User = user,
            FamilyId = familyId,
            TokenHash = IdentitySecurity.HashToken(rawToken),
            ExpiresAt = expiresAt,
            CreatedByIp = Limit(client.IpAddress, 64),
            UserAgent = Limit(client.UserAgent, 500)
        };
        await RefreshTokens.AddAsync(entity, cancellationToken);
        return new IssuedRefreshSession(entity, rawToken);
    }

    private async Task RevokeFamilyAsync(User user, Guid familyId, string reason, ClientContext client, DateTime now, CancellationToken cancellationToken)
    {
        var sessions = await RefreshTokens.Query(tracking: true)
            .Where(x => x.UserId == user.Id && x.FamilyId == familyId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) Revoke(session, reason, client, now);
    }

    private async Task RevokeAllSessionsAsync(long userId, string reason, ClientContext? client, DateTime now, CancellationToken cancellationToken)
    {
        var sessions = await RefreshTokens.Query(tracking: true)
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) Revoke(session, reason, client, now);
    }

    private static void Revoke(RefreshTokenSession session, string reason, ClientContext? client, DateTime now)
    {
        session.RevokedAt = now;
        session.RevokedReason = reason;
        session.RevokedByIp = Limit(client?.IpAddress, 64);
    }

    private AuthSessionResult ToAuthSession(User user, IssuedRefreshSession session)
    {
        var accessToken = tokenIssuer.CreateAccessToken(user, session.Entity.BranchCode);
        return new AuthSessionResult(
            new AuthTokenResponse(accessToken.Value, accessToken.ExpiresAt, session.Entity.BranchCode),
            session.RawToken,
            session.Entity.ExpiresAt);
    }

    private static string NormalizeBranchCode(string? branchCode)
    {
        var normalized = branchCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw AppException.BadRequest("Şube kodu zorunludur.");
        if (normalized.Length > 10)
            throw AppException.BadRequest("Şube kodu en fazla 10 karakter olabilir.");
        return normalized;
    }

    private static AppException InvalidResetToken() =>
        AppException.BadRequest("Şifre yenileme bağlantısı geçersiz veya süresi dolmuş.");

    private async Task InvalidateUndeliveredResetTokenAsync(PasswordResetToken? token)
    {
        if (token is null || token.ConsumedAt.HasValue) return;
        try
        {
            token.ConsumedAt = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Undelivered password reset token could not be invalidated.");
        }
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private sealed record IssuedRefreshSession(RefreshTokenSession Entity, string RawToken);
    private sealed record RefreshOutcome(AuthSessionResult? Session)
    {
        public static RefreshOutcome Valid(AuthSessionResult session) => new(session);
        public static RefreshOutcome Invalid() => new((AuthSessionResult?)null);
    }
}
