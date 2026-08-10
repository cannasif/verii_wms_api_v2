using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using verii_wms_api_v2.Modules.WarehouseAssistant.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseAssistantServiceTests
{
    [Fact]
    public async Task Semantic_ambiguity_returns_clarification_without_running_an_unrelated_query()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        const string clarification = "Stok bakiyesini mi, seri geçmişini mi görmek istiyorsunuz?";
        var service = new WarehouseAssistantService(
            unitOfWork,
            new FixedIntentResolver(new WarehouseAssistantIntentResolution(
                WarehouseAssistantIntent.Unknown,
                WarehouseAssistantDatePreset.Today,
                null,
                null,
                null,
                null,
                false,
                0.41m,
                "semantic-clarification-v2",
                ClarificationQuestion: clarification)),
            new NoopAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero)));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Buna bir bakar mısın?"),
            10,
            "0",
            new WarehouseAssistantAccess(false, true, true, true));

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.Equal(clarification, result.Answer);
        Assert.Empty(result.SerialBalances);
        Assert.Empty(result.Activities);
    }

    [Fact]
    public async Task Capabilities_expose_the_active_assistant_release_and_routing_mode()
    {
        await using var db = CreateDbContext();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(
            unitOfWork,
            new WarehouseAssistantIntentResolver(),
            new NoopAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero)),
            routingDiagnostics: new FixedRoutingDiagnostics());

        var result = await service.GetCapabilitiesAsync(new WarehouseAssistantAccess(false, true, true, true));

        Assert.Equal("2.1.0", result.AssistantVersion);
        Assert.Equal("Hybrid", result.RoutingMode);
        Assert.True(result.SemanticRoutingAvailable);
        Assert.Equal("test-semantic-model", result.SemanticModel);
    }

    [Fact]
    public async Task Last_week_uses_the_previous_complete_monday_to_monday_window()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.AuditLogs.AddRange(
            Activity(10, "previous-week", new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc)),
            Activity(10, "current-week", new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(
            unitOfWork,
            new WarehouseAssistantIntentResolver(),
            new NoopAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Geçen hafta yaptığım işlemleri göster"),
            10,
            "0",
            new WarehouseAssistantAccess(false, false, false, false));

        var activity = Assert.Single(result.Activities);
        Assert.Equal("previous-week", activity.Action);
    }

    [Fact]
    public async Task Misspelled_stock_reference_returns_ranked_choice_and_selected_choice_runs_original_question()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.Set<StockEntity>().AddRange(
            new StockEntity { Id = 50, BranchCode = "0", ErpStockCode = "TEST-001", StockName = "Test Malzeme" },
            new StockEntity { Id = 51, BranchCode = "0", ErpStockCode = "BASKA-001", StockName = "Başka Malzeme" });
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(
            unitOfWork,
            new WarehouseAssistantIntentResolver(),
            new NoopAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero)));

        var clarification = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Tset stoku hangi rafta?"),
            10, "0", new WarehouseAssistantAccess(false, true, false, false));

        var candidate = Assert.Single(clarification.EntityCandidates!);
        Assert.Equal("stock", candidate.EntityType);
        Assert.Equal("TEST-001", candidate.Code);
        Assert.Equal("name", candidate.MatchedBy);
        Assert.Contains("Tset stoku hangi rafta?", candidate.SelectionMessage);

        var resolved = await service.AskAsync(
            new AskWarehouseAssistantRequest(clarification.ConversationId, candidate.SelectionMessage),
            10, "0", new WarehouseAssistantAccess(false, true, false, false));

        Assert.Equal(WarehouseAssistantIntent.StockLocationBalance, resolved.Intent);
        Assert.Empty(resolved.EntityCandidates!);
    }

    [Fact]
    public async Task Misspelled_customer_code_does_not_silently_select_a_goods_receipt_supplier()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.Customers.Add(new Customer { Id = 20, BranchCode = "0", CustomerCode = "ABC", CustomerName = "ABC Tedarik" });
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(
            unitOfWork,
            new WarehouseAssistantIntentResolver(),
            new NoopAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero)));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "ACB carisine kaç mal kabul yapıldı?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, true));

        var candidate = Assert.Single(result.EntityCandidates!);
        Assert.Equal("customer", candidate.EntityType);
        Assert.Equal("ABC", candidate.Code);
        Assert.Empty(result.GoodsReceipts!);
    }

    [Fact]
    public async Task Untyped_goods_receipt_reference_asks_whether_code_is_stock_or_customer()
    {
        await using var db = CreateDbContext();
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.Customers.Add(new Customer { Id = 20, BranchCode = "0", CustomerCode = "ABC", CustomerName = "ABC Tedarik" });
        db.Set<StockEntity>().Add(new StockEntity { Id = 50, BranchCode = "0", ErpStockCode = "ABC", StockName = "ABC Malzeme" });
        await db.SaveChangesAsync();

        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(
            unitOfWork,
            new WarehouseAssistantIntentResolver(),
            new NoopAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero)));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "ABC için kaç mal kabul yapıldı?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, true));

        Assert.Equal(2, result.EntityCandidates!.Count);
        Assert.Contains(result.EntityCandidates, x => x.EntityType == "stock" && x.Code == "ABC");
        Assert.Contains(result.EntityCandidates, x => x.EntityType == "customer" && x.Code == "ABC");
    }

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

    [Fact]
    public async Task Operational_exception_center_reports_balance_integrity_with_evidence()
    {
        await using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.Warehouses.Add(new WarehouseEntity { Id = 30, BranchCode = "0", WarehouseCode = 1, WarehouseName = "Ana Depo" });
        db.Set<WarehouseLocation>().Add(new WarehouseLocation { Id = 40, BranchCode = "0", WarehouseId = 30, Code = "R-01", Name = "Raf 1" });
        db.Set<StockEntity>().Add(new StockEntity { Id = 50, BranchCode = "0", ErpStockCode = "STK-1", StockName = "Test stok" });
        db.Set<LocationStockBalance>().Add(new LocationStockBalance
        {
            Id = 60, BranchCode = "0", WarehouseId = 30, LocationId = 40, StockId = 50,
            DimensionKey = "test", UnitCode = "AD", Quantity = 2, ReservedQuantity = 3,
            AvailableQuantity = -1, LastTransactionDate = now.UtcDateTime.AddMinutes(-5)
        });
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(now));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Müdahale edilmesi gereken operasyon sorunlarını göster"),
            10, "0", new WarehouseAssistantAccess(false, true, false, false));

        Assert.Equal(WarehouseAssistantIntent.OperationalExceptions, result.Intent);
        var issue = Assert.Single(result.Exceptions!);
        Assert.Equal("BALANCE_INTEGRITY", issue.Code);
        Assert.Equal("Critical", issue.Severity);
        Assert.Single(result.Evidence!);
    }

    [Fact]
    public async Task Operational_exception_center_reports_failed_packing_print_jobs_in_branch_scope()
    {
        await using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.Warehouses.Add(new WarehouseEntity { Id = 30, BranchCode = "0", WarehouseCode = 1, WarehouseName = "Ana Depo" });
        db.Set<PackingSession>().Add(new PackingSession
        {
            Id = 70, BranchCode = "0", PackingNo = "PK-2026-0001", WarehouseId = 30,
            PackingStationId = 90, IdempotencyKey = Guid.NewGuid(), OpenedAtUtc = now.AddHours(-1)
        });
        db.Set<HandlingUnit>().Add(new HandlingUnit
        {
            Id = 71, BranchCode = "0", PackingSessionId = 70, PackagingMaterialId = 1,
            HandlingUnitNo = "HU-0001"
        });
        db.Set<PackingPrintJob>().Add(new PackingPrintJob
        {
            Id = 72, BranchCode = "0", HandlingUnitId = 71, PackingStationId = 90,
            Status = PackingPrintJobStatus.Failed, Copies = 1, PayloadJson = "{}",
            IdempotencyKey = Guid.NewGuid(), AttemptCount = 3, RequestedAtUtc = now.AddMinutes(-20),
            LastError = "Printer offline"
        });
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(now));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Müdahale edilmesi gereken operasyon sorunlarını göster"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewPacking: true));

        var issue = Assert.Single(result.Exceptions!);
        Assert.Equal("PACKING_PRINT_FAILED", issue.Code);
        Assert.Equal("PK-2026-0001", issue.DocumentNo);
        Assert.Equal("High", issue.Severity);
    }

    [Fact]
    public async Task Process_blocker_analysis_explains_quality_and_erp_gates()
    {
        await using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.Warehouses.Add(new WarehouseEntity { Id = 30, BranchCode = "0", WarehouseCode = 1, WarehouseName = "Ana Depo" });
        db.GoodsReceiptHeaders.Add(new GoodsReceiptHeader
        {
            Id = 70, BranchCode = "0", DocumentSeriesId = 1, DocumentNo = "GRI-2026-0001",
            DocumentDate = new DateOnly(2026, 8, 9), TargetWarehouseId = 30, ReceivingLocationId = 40,
            Status = WarehouseOperationStatus.Processed, RequireQualityControl = true,
            QualityStatus = OperationQualityStatus.Pending, RequirePutaway = false,
            ErpIntegrationStatus = ErpIntegrationStatus.Failed, CreatedDate = now.UtcDateTime.AddHours(-2)
        });
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(now));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "GRI-2026-0001 belgesi neden tamamlanamıyor?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, true));

        Assert.Equal(WarehouseAssistantIntent.ProcessBlockers, result.Intent);
        Assert.Contains(result.Exceptions!, x => x.Code == "GR_QUALITY");
        Assert.Contains(result.Exceptions!, x => x.Code == "GR_ERP" && x.Severity == "Critical");
    }

    [Fact]
    public async Task Serial_traceability_returns_ordered_movement_ledger_events()
    {
        await using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.Zero);
        db.Users.Add(new User { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" });
        db.Warehouses.Add(new WarehouseEntity { Id = 30, BranchCode = "0", WarehouseCode = 1, WarehouseName = "Ana Depo" });
        db.Set<WarehouseLocation>().Add(new WarehouseLocation { Id = 40, BranchCode = "0", WarehouseId = 30, Code = "R-01", Name = "Raf 1" });
        db.Set<StockEntity>().Add(new StockEntity { Id = 50, BranchCode = "0", ErpStockCode = "STK-1", StockName = "Test stok" });
        db.Set<StockMovementOperation>().Add(new StockMovementOperation
        {
            Id = 80, BranchCode = "0", IdempotencyKey = "trace-1", RequestHash = "hash",
            OperationType = StockMovementTypes.Receipt, Status = StockMovementStatuses.Posted,
            ReferenceType = "GoodsReceipt", ReferenceId = 70, ReferenceNo = "GRI-2026-0001",
            OccurredAt = now.UtcDateTime.AddHours(-1), CreatedBy = 10
        });
        db.Set<StockMovementEntry>().Add(new StockMovementEntry
        {
            Id = 81, BranchCode = "0", OperationId = 80, LineNo = 1, StockId = 50,
            WarehouseId = 30, LocationId = 40, QuantityDelta = 1, UnitCode = "AD",
            SerialNo = "DTG-1", OccurredAt = now.UtcDateTime.AddHours(-1)
        });
        await db.SaveChangesAsync();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = new WarehouseAssistantService(unitOfWork, new WarehouseAssistantIntentResolver(), new NoopAuditWriter(), new FixedTimeProvider(now));

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "DTG-1 serisinin uçtan uca izlenebilirliğini göster"),
            10, "0", new WarehouseAssistantAccess(false, true, true, false));

        Assert.Equal(WarehouseAssistantIntent.Traceability, result.Intent);
        var trace = Assert.Single(result.TraceabilityEvents!);
        Assert.Equal("GRI-2026-0001", trace.DocumentNo);
        Assert.Equal("DTG-1", trace.SerialNo);
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

    private sealed class FixedIntentResolver(WarehouseAssistantIntentResolution resolution) : IWarehouseAssistantIntentResolver
    {
        public Task<WarehouseAssistantIntentResolution> ResolveAsync(
            string message,
            WarehouseAssistantContext? context,
            CancellationToken cancellationToken = default) => Task.FromResult(resolution);
    }

    private sealed class FixedRoutingDiagnostics : IWarehouseAssistantRoutingDiagnostics
    {
        public WarehouseAssistantRoutingInfo GetRoutingInfo() =>
            new("2.1.0", "Hybrid", true, "test-semantic-model");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
