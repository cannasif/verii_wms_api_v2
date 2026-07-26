using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class RefreshTokenReplayPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Allows_rotated_token_from_same_client_inside_grace_window()
    {
        var session = CreateRotatedSession(Now.AddSeconds(-5));

        var allowed = RefreshTokenReplayPolicy.IsAllowed(
            session,
            new ClientContext("127.0.0.1", "WMS-Test-Agent"),
            Now,
            TimeSpan.FromSeconds(15));

        Assert.True(allowed);
    }

    [Fact]
    public void Rejects_rotated_token_after_grace_window()
    {
        var session = CreateRotatedSession(Now.AddSeconds(-16));

        var allowed = RefreshTokenReplayPolicy.IsAllowed(
            session,
            new ClientContext("127.0.0.1", "WMS-Test-Agent"),
            Now,
            TimeSpan.FromSeconds(15));

        Assert.False(allowed);
    }

    [Fact]
    public void Rejects_replay_from_different_client()
    {
        var session = CreateRotatedSession(Now.AddSeconds(-5));

        var allowed = RefreshTokenReplayPolicy.IsAllowed(
            session,
            new ClientContext("127.0.0.1", "Different-Agent"),
            Now,
            TimeSpan.FromSeconds(15));

        Assert.False(allowed);
    }

    [Fact]
    public void Rejects_non_rotation_revocation()
    {
        var session = CreateRotatedSession(Now.AddSeconds(-5));
        session.RevokedReason = "Logout";

        var allowed = RefreshTokenReplayPolicy.IsAllowed(
            session,
            new ClientContext("127.0.0.1", "WMS-Test-Agent"),
            Now,
            TimeSpan.FromSeconds(15));

        Assert.False(allowed);
    }

    private static RefreshTokenSession CreateRotatedSession(DateTime revokedAt) => new()
    {
        RevokedAt = revokedAt,
        RevokedReason = "Rotated",
        ReplacedByTokenHash = "replacement-hash",
        ExpiresAt = Now.AddDays(30),
        CreatedByIp = "127.0.0.1",
        UserAgent = "WMS-Test-Agent"
    };
}
