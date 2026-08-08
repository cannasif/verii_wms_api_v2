using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseAssistantServiceTests
{
    [Fact]
    public async Task Non_admin_all_users_question_is_forced_to_current_user_scope()
    {
        await using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);
        db.Users.AddRange(
            new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" },
            new User { Id = 20, Username = "other", Email = "other@v3rii.com", PasswordHash = "x", Role = "User" });
        db.AuditLogs.AddRange(
            Activity(10, "goods-receipt.task.scan", now.UtcDateTime.AddMinutes(-10)),
            Activity(20, "shipment.task.scan", now.UtcDateTime.AddMinutes(-5)));
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(
            unitOfWork,
            new WarehouseAssistantIntentResolver(),
            new NoopAuditWriter(),
            new FixedTimeProvider(now));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Herkesin bugün yaptığı işlemleri göster"),
            10,
            "0",
            new WarehouseAssistantAccess(false, false, false, false));

        Assert.Equal("self", result.Scope);
        Assert.Single(result.Activities);
        Assert.Equal(10, result.Activities[0].UserId);
        Assert.DoesNotContain(result.Activities, x => x.UserId == 20);
    }

    private static WmsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new WmsDbContext(options);
    }

    private static AuditLog Activity(long userId, string action, DateTime createdDate) => new()
    {
        BranchCode = "0",
        TraceId = Guid.NewGuid().ToString("N"),
        ActionType = action,
        EntityType = "TestEntity",
        EntityId = userId.ToString(),
        Result = "Succeeded",
        Source = "test",
        PerformedByUserId = userId,
        CreatedBy = userId,
        CreatedDate = createdDate
    };

    private sealed class NoopAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
