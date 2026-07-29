using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class IdentitySessionRefreshTests
{
    [Fact]
    public async Task Repeated_refresh_reuses_the_same_database_session()
    {
        await using var db = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor());

        var rawToken = IdentitySecurity.CreateOpaqueToken();
        var user = new User
        {
            Username = "session-test",
            Email = "session-test@test.local",
            PasswordHash = "not-used",
            IsActive = true
        };
        var session = new RefreshTokenSession
        {
            BranchCode = "100",
            User = user,
            FamilyId = Guid.NewGuid(),
            TokenHash = IdentitySecurity.HashToken(rawToken),
            ExpiresAt = DateTime.UtcNow.AddDays(60)
        };
        db.Add(session);
        await db.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:RefreshTokenDays"] = "90",
                ["Identity:RefreshTokenRenewalWindowDays"] = "30"
            })
            .Build();
        var service = new IdentityService(
            unitOfWork,
            new TokenIssuerStub(),
            new SessionValidatorStub(),
            new PasswordPolicyStub(),
            new EmailSenderStub(),
            configuration,
            NullLogger<IdentityService>.Instance);

        var first = await service.RefreshAsync(rawToken, new ClientContext("127.0.0.1", "test"));
        var second = await service.RefreshAsync(rawToken, new ClientContext("127.0.0.1", "test"));

        Assert.Equal(rawToken, first.RefreshToken);
        Assert.Equal(rawToken, second.RefreshToken);
        Assert.Equal("100", second.Response.BranchCode);
        Assert.Equal(1, await db.RefreshTokenSessions.IgnoreQueryFilters().CountAsync());
    }

    private sealed class TokenIssuerStub : ITokenIssuer
    {
        public AccessTokenResult CreateAccessToken(User user, string branchCode) =>
            new($"access-{user.Id}-{branchCode}", DateTime.UtcNow.AddHours(8));
    }

    private sealed class SessionValidatorStub : IIdentitySessionValidator
    {
        public Task<bool> IsValidAsync(long userId, int tokenVersion) => Task.FromResult(true);
        public void Invalidate(long userId) { }
    }

    private sealed class PasswordPolicyStub : IPasswordPolicyService
    {
        public Task<PasswordPolicyResponse> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PasswordPolicyResponse(6, 15));

        public Task ValidateAsync(string? password, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmailSenderStub : IIdentityEmailSender
    {
        public Task SendPasswordResetAsync(
            string recipientEmail,
            string resetUrl,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
