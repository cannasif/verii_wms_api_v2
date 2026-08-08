using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using verii_wms_api_v2.Modules.WarehouseAssistant.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
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

    [Fact]
    public async Task Steel_vehicle_analysis_returns_branch_scoped_plate_and_acceptance_totals()
    {
        await using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        db.Users.Add(new User { Id = 10, Username = "admin", Email = "admin@v3rii.com", PasswordHash = "x", Role = "Admin" });
        db.Set<VehicleCheckInHeader>().AddRange(
            Vehicle(100, "0", "34 ABC 123", 8, now.AddHours(-2)),
            Vehicle(101, "0", "06 XYZ 987", 4, now.AddHours(-1)),
            Vehicle(102, "0", "35 OLD 001", 7, now.AddDays(-1)),
            Vehicle(103, "1", "34 OTHER 9", 9, now.AddHours(-1)),
            Vehicle(104, "0", "34 ABC 123", 3, now.AddDays(-2)));
        db.Set<SteelVehicleAcceptedPlate>().AddRange(
            AcceptedPlate(200, 100, SteelPlateIdentityStatus.Known),
            AcceptedPlate(201, 100, SteelPlateIdentityStatus.Unknown),
            AcceptedPlate(202, 101, SteelPlateIdentityStatus.Known));
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(now));

        var all = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Bugün sac mal kabul için kaç araç girdi, plakaları neler?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewSteelVehicles: true));
        var onePlate = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Bugün plaka 34 ABC 123 olan sac aracı kaç levhayla girdi?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewSteelVehicles: true));
        var plateHistory = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "34 ABC 123 plakasının sac mal kabul geçmişini göster"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewSteelVehicles: true));

        Assert.Equal(WarehouseAssistantIntent.SteelVehicleAnalysis, all.Intent);
        Assert.Equal(2, all.SteelVehicles!.Count);
        Assert.Equal(12, all.SteelVehicles.Sum(x => x.DeclaredSteelSheetCount));
        Assert.Equal(3, all.SteelVehicles.Sum(x => x.AcceptedPlateCount));
        Assert.Equal(1, all.SteelVehicles.Sum(x => x.UnresolvedPlateCount));
        Assert.Equal("34 ABC 123", Assert.Single(onePlate.SteelVehicles!).PlateNo);
        Assert.Equal(2, plateHistory.SteelVehicles!.Count);
    }

    [Fact]
    public async Task Transfer_analysis_separates_production_and_interwarehouse_documents()
    {
        await using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        db.Users.Add(new User { Id = 10, Username = "admin", Email = "admin@v3rii.com", PasswordHash = "x", Role = "Admin" });
        db.Warehouses.AddRange(
            new WarehouseEntity { Id = 30, BranchCode = "0", WarehouseCode = 1, WarehouseName = "Hammadde" },
            new WarehouseEntity { Id = 31, BranchCode = "0", WarehouseCode = 2, WarehouseName = "Üretim" });
        db.Set<WarehouseTransferHeader>().AddRange(
            TransferHeader(300, "WT-2026-001", WarehouseTransferBusinessContext.InterWarehouse, WarehouseTransferStatus.Completed),
            TransferHeader(301, "PT-2026-001", WarehouseTransferBusinessContext.ProductionMaterialSupply, WarehouseTransferStatus.PartiallyPicked),
            TransferHeader(302, "PT-2025-OLD", WarehouseTransferBusinessContext.ProductionMaterialSupply, WarehouseTransferStatus.Completed, new DateOnly(2025, 8, 8)));
        db.Set<WarehouseTransferLine>().AddRange(
            TransferLine(400, 300, 10, 10, 10),
            TransferLine(401, 301, 5, 2, 0),
            TransferLine(402, 302, 7, 7, 7));
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(now));
        var access = new WarehouseAssistantAccess(
            false, false, false, false,
            CanViewWarehouseTransfers: true,
            CanViewProductionTransfers: true);

        var production = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Bugün kaç üretime transfer yapıldı?"), 10, "0", access);
        var warehouse = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Bugünkü normal depolar arası transferleri göster"), 10, "0", access);
        var historicalDocument = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "PT-2025-OLD numaralı üretime transferin durumunu göster"), 10, "0", access);
        var todayHistoricalDocument = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Bugün PT-2025-OLD numaralı üretime transferi göster"), 10, "0", access);

        var productionRow = Assert.Single(production.Transfers!);
        Assert.Equal("PT-2026-001", productionRow.DocumentNo);
        Assert.Equal(5, productionRow.RequestedQuantity);
        Assert.Equal(2, productionRow.PickedQuantity);
        var warehouseRow = Assert.Single(warehouse.Transfers!);
        Assert.Equal("WT-2026-001", warehouseRow.DocumentNo);
        Assert.Equal(10, warehouseRow.ReceivedQuantity);
        Assert.Equal("PT-2025-OLD", Assert.Single(historicalDocument.Transfers!).DocumentNo);
        Assert.Empty(todayHistoricalDocument.Transfers!);
        Assert.Equal(WarehouseAssistantTransferScope.Production,
            (await new WarehouseAssistantIntentResolver().ResolveAsync("Bugün kaç üretime transfer yapıldı?", null)).TransferScope);
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

    private static VehicleCheckInHeader Vehicle(long id, string branch, string plate, int sheetCount, DateTimeOffset checkedInAt) => new()
    {
        Id = id,
        BranchCode = branch,
        PlateNo = plate,
        PlateNoNormalized = new string(plate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray()),
        SteelSheetCount = sheetCount,
        BusinessDate = DateOnly.FromDateTime(checkedInAt.UtcDateTime),
        CheckedInAtUtc = checkedInAt,
        Status = VehicleCheckInStatus.Completed
    };

    private static SteelVehicleAcceptedPlate AcceptedPlate(long id, long vehicleId, SteelPlateIdentityStatus status) => new()
    {
        Id = id,
        BranchCode = "0",
        VehicleCheckInId = vehicleId,
        VehicleAcceptanceId = 900,
        SequenceNo = (int)(id - 199),
        IdentityStatus = status
    };

    private static WarehouseTransferHeader TransferHeader(
        long id,
        string documentNo,
        WarehouseTransferBusinessContext context,
        WarehouseTransferStatus status,
        DateOnly? documentDate = null) => new()
    {
        Id = id,
        BranchCode = "0",
        DocumentSeriesId = 1,
        DocumentNo = documentNo,
        DocumentDate = documentDate ?? new DateOnly(2026, 8, 8),
        BusinessContext = context,
        InitiationMode = WarehouseTransferInitiationMode.StockBasedTask,
        ProcessType = WarehouseTransferProcessType.InternalRequest,
        SourceWarehouseId = 30,
        TargetWarehouseId = 31,
        Status = status
    };

    private static WarehouseTransferLine TransferLine(long id, long headerId, decimal requested, decimal picked, decimal received) => new()
    {
        Id = id,
        BranchCode = "0",
        WtHeaderId = headerId,
        LineNo = 1,
        StockId = 500,
        StockCodeSnapshot = "STK-1",
        StockNameSnapshot = "Test Stok",
        UnitCode = "AD",
        BaseUnitCode = "AD",
        RequestedQuantity = requested,
        PickedQuantity = picked,
        ShippedQuantity = received,
        ReceivedQuantity = received,
        PutawayQuantity = received,
        SourceWarehouseId = 30,
        TargetWarehouseId = 31
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
