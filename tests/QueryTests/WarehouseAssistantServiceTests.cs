using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using verii_wms_api_v2.Modules.WarehouseAssistant.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseAssistantServiceTests
{
    [Fact]
    public async Task Goods_receipt_analysis_filters_by_document_date_supplier_and_received_quantity()
    {
        await using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);
        db.Users.Add(new User { Id = 10, Username = "admin", Email = "admin@v3rii.com", PasswordHash = "x", Role = "Admin" });
        db.Customers.Add(new Customer { Id = 20, BranchCode = "0", CustomerCode = "ABC", CustomerName = "ABC TEDARIK" });
        db.Warehouses.Add(new WarehouseEntity { Id = 30, BranchCode = "0", WarehouseCode = 1, WarehouseName = "Ana Depo" });
        db.GoodsReceiptHeaders.AddRange(
            ReceiptHeader(100, new DateOnly(2026, 8, 3), WarehouseOperationStatus.Completed),
            ReceiptHeader(101, new DateOnly(2026, 7, 31), WarehouseOperationStatus.Completed),
            ReceiptHeader(102, new DateOnly(2026, 8, 4), WarehouseOperationStatus.Cancelled));
        db.GoodsReceiptLines.AddRange(
            ReceiptLine(200, 100, 5),
            ReceiptLine(201, 101, 7),
            ReceiptLine(202, 102, 9),
            ReceiptLine(203, 100, 0));
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(now));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "01.08.2026 ile 08.08.2026 arasında ABC carisine kaç mal kabul yapıldı, neler alındı?"),
            10,
            "0",
            new WarehouseAssistantAccess(false, false, false, true));

        Assert.Equal(WarehouseAssistantIntent.GoodsReceiptAnalysis, result.Intent);
        Assert.Equal("authorized-warehouses", result.Scope);
        var row = Assert.Single(result.GoodsReceipts!);
        Assert.Equal(100, row.GoodsReceiptId);
        Assert.Equal(5, row.ReceivedQuantity);
        Assert.Equal("ABC", row.SupplierCode);
    }

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

    [Fact]
    public async Task Conversation_history_restores_structured_result_cards()
    {
        await using var db = CreateDbContext();
        var now = new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc);
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.WarehouseAssistantConversations.Add(new WarehouseAssistantConversation
        {
            Id = 100,
            UserId = 10,
            Title = "Görevlerim",
            LastMessageAtUtc = now,
            BranchCode = "0",
            CreatedDate = now
        });
        db.WarehouseAssistantMessages.Add(new WarehouseAssistantMessage
        {
            Id = 101,
            ConversationId = 100,
            Role = "assistant",
            Content = "Bir görev bulundu.",
            Intent = WarehouseAssistantIntent.AssignedTasks.ToString(),
            Scope = "self",
            ResponseDataJson = """{"providerMode":"deterministic","activities":[],"serialBalances":[],"serialReceipts":[],"stockLocations":[],"barcode":null,"movements":[],"tasks":[{"module":"GoodsReceipt","taskId":5,"taskNo":"GR-TASK-5","taskType":"Receive","status":"Assigned","priority":1,"documentId":7,"documentNo":"GR-7","warehouseId":2,"warehouseCode":1,"warehouseName":"Ana Depo","plannedQuantity":10,"processedQuantity":4,"remainingQuantity":6,"plannedAtUtc":null,"dueAtUtc":null,"assigneeUserId":10,"assigneeDisplayName":"worker"}],"suggestions":[]}""",
            CorrelationId = Guid.NewGuid(),
            BranchCode = "0",
            CreatedDate = now
        });
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(new DateTimeOffset(now)));

        var rows = await service.GetMessagesAsync(100, 10, "0");

        var result = Assert.Single(rows).Result;
        Assert.NotNull(result);
        Assert.Equal("GR-TASK-5", Assert.Single(result.Tasks).TaskNo);
    }

    [Fact]
    public async Task Archive_only_hides_owned_conversation()
    {
        await using var db = CreateDbContext();
        var now = new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc);
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.WarehouseAssistantConversations.Add(new WarehouseAssistantConversation
        {
            Id = 100,
            UserId = 10,
            Title = "Görevlerim",
            LastMessageAtUtc = now,
            BranchCode = "0",
            CreatedDate = now
        });
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(new DateTimeOffset(now)));

        await service.ArchiveConversationAsync(100, 10, "0");

        Assert.Empty(await service.GetConversationsAsync(10, "0"));
        Assert.True((await db.WarehouseAssistantConversations.IgnoreQueryFilters().SingleAsync(x => x.Id == 100)).IsArchived);
    }

    [Theory]
    [InlineData("Barkod GRL-000123 hangi stoka ait?")]
    [InlineData("01/013 stok hareketlerini göster")]
    [InlineData("Bana atanmış açık görevleri göster")]
    [InlineData("01.08.2026 ile 08.08.2026 arasında kaç mal kabul yapıldı?")]
    public async Task Operational_queries_fail_closed_without_module_permissions(string question)
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(
            unitOfWork,
            new WarehouseAssistantIntentResolver(),
            new NoopAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero)));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, question),
            10,
            "0",
            new WarehouseAssistantAccess(false, false, false, false));

        Assert.Equal("denied", result.Scope);
        Assert.Empty(result.StockLocations);
        Assert.Empty(result.Movements);
        Assert.Empty(result.Tasks);
        Assert.Null(result.Barcode);
    }

    [Fact]
    public async Task Valid_parameter_hint_returns_safe_catalog_reference_without_database_query()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(
            unitOfWork,
            new WarehouseAssistantIntentResolver(),
            new NoopAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 8, 10, 0, 0, TimeSpan.Zero)));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(
                null,
                "Kalite bekleyen üründe hangi raflar seçilebilir ayarını açarsam ne olur?",
                new WarehouseAssistantParameterHint("goodsReceipt", "blockPutawayUntilQualityDecision", "true")),
            10,
            "0",
            new WarehouseAssistantAccess(false, false, false, false));

        Assert.Equal(WarehouseAssistantIntent.ParameterHelp, result.Intent);
        var guide = Assert.Single(result.ParameterGuides!);
        Assert.Equal("goodsReceipt", guide.Module);
        Assert.Equal("blockPutawayUntilQualityDecision", guide.Field);
        Assert.Equal("true", guide.Value);
    }

    [Fact]
    public async Task User_cannot_archive_another_users_conversation()
    {
        await using var db = CreateDbContext();
        var now = new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc);
        db.Users.AddRange(
            new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" },
            new User { Id = 20, Username = "other", Email = "other@v3rii.com", PasswordHash = "x", Role = "User" });
        db.WarehouseAssistantConversations.Add(new WarehouseAssistantConversation
        {
            Id = 100,
            UserId = 20,
            Title = "Other user's conversation",
            LastMessageAtUtc = now,
            BranchCode = "0",
            CreatedDate = now
        });
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(new DateTimeOffset(now)));

        var exception = await Assert.ThrowsAsync<AppException>(() => service.ArchiveConversationAsync(100, 10, "0"));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.False((await db.WarehouseAssistantConversations.SingleAsync(x => x.Id == 100)).IsArchived);
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

    private static GoodsReceiptHeader ReceiptHeader(long id, DateOnly date, WarehouseOperationStatus status) => new()
    {
        Id = id,
        BranchCode = "0",
        DocumentNo = $"GR-{id}",
        DocumentDate = date,
        SupplierId = 20,
        SupplierCodeSnapshot = "ABC",
        SupplierNameSnapshot = "ABC TEDARIK",
        TargetWarehouseId = 30,
        ReceivingLocationId = 1,
        Status = status,
        ReceivedAtUtc = date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc),
        ReceivedBy = 10
    };

    private static GoodsReceiptLine ReceiptLine(long id, long headerId, decimal quantity) => new()
    {
        Id = id,
        BranchCode = "0",
        GrHeaderId = headerId,
        LineNo = 1,
        StockId = 500,
        StockCodeSnapshot = "STK-1",
        StockNameSnapshot = "Test Stok",
        UnitCode = "AD",
        BaseUnitCode = "AD",
        TargetWarehouseId = 30,
        ReceivedQuantity = quantity,
        AcceptedQuantity = quantity,
        Status = GoodsReceiptLineStatus.Received
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
