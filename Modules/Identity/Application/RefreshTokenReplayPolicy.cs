using verii_wms_api_v2.Modules.Identity.Domain;

namespace verii_wms_api_v2.Modules.Identity.Application;

internal static class RefreshTokenReplayPolicy
{
    internal static bool IsAllowed(
        RefreshTokenSession session,
        ClientContext client,
        DateTime now,
        TimeSpan graceWindow)
    {
        if (graceWindow <= TimeSpan.Zero
            || session.RevokedReason != "Rotated"
            || !session.RevokedAt.HasValue
            || session.RevokedAt.Value > now
            || session.RevokedAt.Value < now.Subtract(graceWindow)
            || session.ExpiresAt <= now
            || string.IsNullOrWhiteSpace(session.ReplacedByTokenHash)
            || string.IsNullOrWhiteSpace(session.UserAgent)
            || string.IsNullOrWhiteSpace(client.UserAgent))
            return false;

        return string.Equals(session.CreatedByIp, Limit(client.IpAddress, 64), StringComparison.Ordinal)
            && string.Equals(session.UserAgent, Limit(client.UserAgent, 500), StringComparison.Ordinal);
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
