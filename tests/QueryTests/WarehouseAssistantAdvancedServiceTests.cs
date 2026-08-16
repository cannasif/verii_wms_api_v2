using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.InventoryCount.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseAssistantAdvancedServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Warehouse_overview_never_reveals_an_unassigned_warehouse()
    {
        await using var db = CreateDbContext();
        db.Users.Add(User());
        db.Warehouses.AddRange(
            Warehouse(30, "0", 10, "Yetkili Depo"),
            Warehouse(31, "0", 20, "Gizli Depo"));
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment { Id = 40, BranchCode = "0", UserId = 10, WarehouseId = 30 });
        await db.SaveChangesAsync();
        await using var unitOfWork = UnitOfWork(db);
        var service = Service(unitOfWork);

        var result = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Depolar hangileri?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewLocations: true));

        var row = Assert.Single(result.AnalysisRows!);
        Assert.Equal("10", row.Code);
        Assert.DoesNotContain(result.AnalysisRows!, x => x.Name == "Gizli Depo");
        Assert.Equal("authorized-branch-warehouse-scope", result.Scope);
    }

    [Fact]
    public async Task Location_capacity_does_not_combine_incompatible_units()
    {
        await using var db = CreateDbContext();
        db.Users.Add(User());
        db.Warehouses.Add(Warehouse(30, "0", 10, "Ana Depo"));
        db.Set<WarehouseLocation>().Add(new WarehouseLocation
        {
            Id = 40, BranchCode = "0", WarehouseId = 30, Code = "A01/R01-G01", Name = "Göz 1",
            CapacityQuantity = 100, CapacityUnit = "KG"
        });
        db.Set<StockEntity>().Add(new StockEntity { Id = 50, BranchCode = "0", ErpStockCode = "STK-1", StockName = "Stok", BaseUnitCode = "AD" });
        db.Set<LocationStockBalance>().Add(new LocationStockBalance
        {
            Id = 60, BranchCode = "0", WarehouseId = 30, LocationId = 40, StockId = 50,
            UnitCode = "AD", Quantity = 20, AvailableQuantity = 15, ReservedQuantity = 5, LastTransactionDate = Now.UtcDateTime
        });
        await db.SaveChangesAsync();
        await using var unitOfWork = UnitOfWork(db);

        var result = await Service(unitOfWork).AskAsync(
            new AskWarehouseAssistantRequest(null, "A01/R01-G01 kapasitesi ve doluluğu nedir?"),
            10, "0", new WarehouseAssistantAccess(false, true, false, false, CanViewLocations: true));

        var row = Assert.Single(result.AnalysisRows!);
        Assert.Equal("AD", row.UnitCode);
        Assert.Equal("KG", row.CapacityUnit);
        Assert.Null(row.CapacityQuantity);
        Assert.Contains("doluluk oranı hesaplanmadı", row.Detail);
    }

    [Fact]
    public async Task Inventory_variance_requires_review_permission_before_reading_lines()
    {
        await using var db = CreateDbContext();
        db.Users.Add(User());
        db.Warehouses.Add(Warehouse(30, "0", 10, "Ana Depo"));
        db.Set<InventoryCountLine>().Add(new InventoryCountLine
        {
            Id = 80, BranchCode = "0", HeaderId = 70, TaskId = 71, WarehouseId = 30, LocationId = 40, StockId = 50,
            UnitCode = "AD", SnapshotQuantity = 10, CountedQuantity = 7, VarianceQuantity = -3, Status = InventoryCountLineStatus.Variance
        });
        await db.SaveChangesAsync();
        await using var unitOfWork = UnitOfWork(db);

        var result = await Service(unitOfWork).AskAsync(
            new AskWarehouseAssistantRequest(null, "Sayım farkı olan ürünler nelerdir?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewInventoryCounts: true));

        Assert.Equal("denied", result.Scope);
        Assert.Empty(result.AnalysisRows!);
        Assert.Contains("inceleme yetkisi", result.Answer);
    }

    [Fact]
    public async Task Generator_projects_are_permission_and_branch_scoped()
    {
        await using var db = CreateDbContext();
        db.Users.Add(User());
        db.Set<GeneratorProductionProject>().AddRange(
            Project(90, "0", "PRJ-001", "Görünen Proje"),
            Project(91, "OTHER", "PRJ-SECRET", "Başka Şube"));
        await db.SaveChangesAsync();
        await using var unitOfWork = UnitOfWork(db);
        var service = Service(unitOfWork);

        var denied = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Aktif jeneratör üretim projeleri hangileri?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false));
        var allowed = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Aktif jeneratör üretim projeleri hangileri?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewGeneratorProduction: true));

        Assert.Equal("denied", denied.Scope);
        var row = Assert.Single(allowed.AnalysisRows!);
        Assert.Equal("PRJ-001", row.Code);
    }

    [Fact]
    public async Task Navigation_help_uses_verified_routes_and_never_performs_the_write()
    {
        await using var db = CreateDbContext();
        db.Users.Add(User());
        await db.SaveChangesAsync();
        await using var unitOfWork = UnitOfWork(db);
        var service = Service(unitOfWork);

        var stock = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Yeni ürün nasıl eklenir?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewErpMirror: true));
        var transfer = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Transfer nasıl başlatılır?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false));

        Assert.Equal("/erp/stocks", Assert.Single(stock.AnalysisRows!).Route);
        Assert.Contains("WMS stok kartı oluşturmaz", stock.Answer);
        var deniedRoute = Assert.Single(transfer.AnalysisRows!);
        Assert.Equal("Denied", deniedRoute.Status);
        Assert.Null(deniedRoute.Route);
    }

    [Fact]
    public async Task Analysis_rows_are_persisted_and_restored_with_conversation_history()
    {
        await using var db = CreateDbContext();
        db.Users.Add(User());
        db.Warehouses.Add(Warehouse(30, "0", 10, "Ana Depo"));
        await db.SaveChangesAsync();
        await using var unitOfWork = UnitOfWork(db);
        var service = Service(unitOfWork);

        var response = await service.AskAsync(
            new AskWarehouseAssistantRequest(null, "Depolar hangileri?"),
            10, "0", new WarehouseAssistantAccess(false, false, false, false, CanViewLocations: true));
        var history = await service.GetMessagesAsync(response.ConversationId, 10, "0");

        var restored = Assert.Single(history, x => x.Role == "assistant").Result;
        Assert.Equal("10", Assert.Single(restored!.AnalysisRows!).Code);
        Assert.Equal(WarehouseAssistantQueryKind.WarehouseList, Assert.Single(restored.Interpretations!).QueryKind);
    }

    private static WmsDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<WmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static UnitOfWork UnitOfWork(WmsDbContext db) =>
        new(db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

    private static WarehouseAssistantService Service(UnitOfWork unitOfWork) => new(
        unitOfWork,
        new LocalHybridWarehouseAssistantIntentResolver(
            new WarehouseAssistantIntentResolver(),
            Microsoft.Extensions.Options.Options.Create(new WarehouseAssistantOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalHybridWarehouseAssistantIntentResolver>.Instance),
        new NoopAuditWriter(),
        new FixedTimeProvider(Now));

    private static User User() => new() { Id = 10, Username = "worker", Email = "worker@v3rii.com", PasswordHash = "x", Role = "User" };

    private static WarehouseEntity Warehouse(long id, string branch, int code, string name) => new()
    {
        Id = id, BranchCode = branch, WarehouseCode = code, WarehouseName = name
    };

    private static GeneratorProductionProject Project(long id, string branch, string code, string name) => new()
    {
        Id = id, BranchCode = branch, ProjectCode = code, ProjectName = name,
        Status = GeneratorProjectStatus.InProgress, Quantity = 1,
        PlannedStartAtUtc = Now.UtcDateTime.AddDays(-1), PlannedDeliveryAtUtc = Now.UtcDateTime.AddDays(1)
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
