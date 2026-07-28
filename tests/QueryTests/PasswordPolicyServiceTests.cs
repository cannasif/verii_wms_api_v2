using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.ProjectSettings.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class PasswordPolicyServiceTests
{
    [Fact]
    public async Task Project_setting_cannot_be_raised_above_shortest_recorded_password()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User
        {
            Username = "six.chars",
            Email = "six@firma.com",
            PasswordHash = "hash",
            PasswordLength = 6
        });
        await db.SaveChangesAsync();
        var service = CreateProjectSettingsService(db);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            service.UpdateAsync(Request(7)));

        Assert.Contains("en fazla 6", exception.Message);
        var saved = await service.UpdateAsync(Request(5));
        Assert.Equal(5, saved.PasswordMinimumLength);
        Assert.Equal(15, saved.PasswordMaximumLength);
    }

    [Theory]
    [InlineData("12345", 6, false)]
    [InlineData("123456", 6, true)]
    [InlineData("123456789012345", 6, true)]
    [InlineData("1234567890123456", 6, false)]
    public void Password_validation_uses_configured_minimum_and_fixed_maximum(
        string password,
        int minimumLength,
        bool isValid)
    {
        var exception = Record.Exception(() => IdentitySecurity.ValidatePassword(password, minimumLength));
        Assert.Equal(isValid, exception is null);
    }

    private static ProjectSettingsService CreateProjectSettingsService(WmsDbContext db) =>
        new(
            new UnitOfWork(db, new HttpContextAccessor()),
            new MemoryCache(new MemoryCacheOptions()),
            new NoOpAuditWriter());

    private static UpdateProjectSettingsRequest Request(int passwordMinimumLength) =>
        new("tr-TR", 2, "dd.MM.yyyy", "HH:mm", "yyyy", "Europe/Istanbul", true, passwordMinimumLength);

    private static WmsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new WmsDbContext(options);
    }

    private sealed class NoOpAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
