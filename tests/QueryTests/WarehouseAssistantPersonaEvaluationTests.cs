using System.Globalization;
using System.Resources;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.InventoryCount.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using verii_wms_api_v2.Modules.WarehouseAssistant.Localization;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using Xunit;
using Xunit.Abstractions;

namespace verii_wms_api_v2.QueryTests;

/// <summary>
/// Characterization corpus for reviewing the actual end-user answer, not only the intent.
/// It deliberately contains both WMS terminology and colloquial/novice Turkish. The test
/// emits every real AskAsync response and grades it against deterministic fixture facts.
/// </summary>
public sealed class WarehouseAssistantPersonaEvaluationTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Persona_questions_record_actual_answers_and_domain_correctness()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        await using var unitOfWork = new UnitOfWork(
            db,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var service = CreateService(unitOfWork);
        var results = new List<EvaluationResult>();
        var conversations = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var item in Cases)
        {
            var conversationId = item.ConversationKey is not null
                && conversations.TryGetValue(item.ConversationKey, out var existingConversationId)
                    ? existingConversationId
                    : (long?)null;
            var response = await service.AskAsync(
                new AskWarehouseAssistantRequest(conversationId, item.Question),
                10,
                "0",
                FullAccess);
            if (item.ConversationKey is not null)
                conversations[item.ConversationKey] = response.ConversationId;
            var actualKind = response.Interpretations?.FirstOrDefault()?.QueryKind ?? WarehouseAssistantQueryKind.None;
            var intentCorrect = response.Intent == item.ExpectedIntent;
            var planCorrect = actualKind == item.ExpectedQueryKind;
            var dataCorrect = ValidateDomainFact(item.Id, response);
            var result = new EvaluationResult(
                item.Id,
                item.Persona,
                item.Question,
                item.ExpectedIntent.ToString(),
                item.ExpectedQueryKind.ToString(),
                response.Intent.ToString(),
                actualKind.ToString(),
                response.Answer,
                response.Scope,
                response.ProviderMode,
                DescribeRows(response),
                intentCorrect,
                planCorrect,
                dataCorrect,
                intentCorrect && planCorrect && dataCorrect);
            results.Add(result);
            output.WriteLine("PERSONA_EVAL " + JsonSerializer.Serialize(result, JsonOptions));
        }

        foreach (var group in results.GroupBy(x => x.Persona))
        {
            output.WriteLine("PERSONA_SUMMARY " + JsonSerializer.Serialize(new
            {
                persona = group.Key,
                total = group.Count(),
                correct = group.Count(x => x.Correct),
                intentCorrect = group.Count(x => x.IntentCorrect),
                planCorrect = group.Count(x => x.PlanCorrect),
                dataCorrect = group.Count(x => x.DataCorrect)
            }, JsonOptions));
        }

        Assert.Equal(Cases.Count, results.Count);
        Assert.DoesNotContain(results, result => !result.Correct);
    }

    private static bool ValidateDomainFact(string id, WarehouseAssistantChatResponse response) => id switch
    {
        "EXP-01" or "NOV-01" or "NOV-21" or "NOV-22" => response.SummaryMetrics?.Any(x => x.Key == "warehouseCount" && x.Value == 2) == true,
        "EXP-02" or "NOV-02" or "NOV-23" or "NOV-42" or "NOV-57" => HasAnalysisCodes(response, "A01/R01-G01", "A01/R01-G02", "KRN-01")
            && response.AnalysisRows?.All(x => x.WarehouseCode == 10) == true,
        "EXP-03" => response.AnalysisRows?.Count == 1
            && response.AnalysisRows.Any(x => x.WarehouseCode == 10 && x.UnitCode == "AD"
                && x.PhysicalQuantity == 110 && x.AvailableQuantity == 82 && x.ReservedQuantity == 28),
        "EXP-04" or "NOV-04" or "NOV-43" or "NOV-55" => HasAnalysisCodes(response, "STK-A", "STK-B")
            && response.AnalysisRows?.Sum(x => x.PhysicalQuantity) == 80,
        "EXP-05" or "NOV-06" or "NOV-56" => response.AnalysisRows?.Count == 1
            && response.AnalysisRows.Any(x => x.LocationCode == "A01/R01-G01"
                && x.CapacityQuantity == 100 && x.PhysicalQuantity == 80 && x.CapacityUnit == "AD"),
        "EXP-06" => HasAnalysisCodes(response, "KRN-01") && response.AnalysisRows?.Count == 1,
        "EXP-07" or "NOV-07" or "NOV-25" or "NOV-45" => HasAnalysisCodes(response, "STK-C") && response.AnalysisRows?.Count == 1,
        "EXP-08" or "NOV-08" or "NOV-26" => response.AnalysisRows?.FirstOrDefault()?.Code == "STK-A"
            && response.AnalysisRows?.FirstOrDefault()?.PhysicalQuantity == 125,
        "EXP-09" or "NOV-10" or "NOV-28" or "NOV-47" => HasAnalysisCodes(response, "STK-A", "STK-B")
            && response.AnalysisRows?.Count == 2,
        "EXP-10" or "NOV-16" => response.Answer.Contains("kritik/minimum stok eşiği", StringComparison.OrdinalIgnoreCase)
            && response.AnalysisRows?.Count == 0,
        "EXP-11" or "NOV-11" or "NOV-29" or "NOV-31" => HasAnalysisCodes(response, "CNT-2026-001") && response.AnalysisRows?.Count == 1,
        "EXP-12" or "NOV-12" or "NOV-30" or "NOV-48" => response.AnalysisRows?.Any(x => x.Code == "STK-A" && x.VarianceQuantity == -5) == true,
        "EXP-13" => HasAnalysisCodes(response, "PRJ-001") && response.AnalysisRows?.Count == 1,
        "EXP-14" or "NOV-13" or "NOV-32" or "NOV-49" or "NOV-60" => response.AnalysisRows?.Any(x => x.Code == "OP-WELD"
            && x.Detail?.Contains("Malzeme eksiği", StringComparison.OrdinalIgnoreCase) == true) == true,
        "EXP-15" or "NOV-33" => response.AnalysisRows?.Any(x => x.Code == "OP-WELD"
            && x.Detail?.Contains("Kalite: Pending", StringComparison.OrdinalIgnoreCase) == true) == true,
        "EXP-16" or "NOV-34" => response.AnalysisRows?.Any(x => x.Code == "PRJ-001"
            && x.PlannedQuantity == 2 && x.ActualQuantity == 1) == true,
        "EXP-17" or "NOV-14" => response.AnalysisRows?.Any(x => x.Code == "OP-WELD") == true
            && response.AnalysisRows?.Count == 1,
        "EXP-18" => HasNavigation(response, "/warehouse/stock-movements",
            "Depo(Ambar) İşlemleri → Depo Yönetimi → Stok Hareketleri"),
        "EXP-19" => response.AnalysisRows?.Count == 0
            && response.Answer.Contains("eşleşen depo bulunamadı", StringComparison.OrdinalIgnoreCase),
        "EXP-20" or "NOV-20" or "NOV-61" or "NOV-62" or "NOV-63" or "NOV-64" => response.Intent == WarehouseAssistantIntent.Unknown
            && response.ProviderMode.Contains("write-rejected", StringComparison.OrdinalIgnoreCase)
            && response.Answer.Contains("salt okunur", StringComparison.OrdinalIgnoreCase)
            && TotalRows(response) == 0,
        "NOV-03" => response.StockLocations?.Any(x => x.StockCode == "STK-A" && x.Quantity == 70) == true,
        "NOV-05" or "NOV-24" or "NOV-44" => response.AnalysisRows?.Any(x => x.LocationCode == "A01/R01-G02" && x.Status == "Empty") == true,
        "NOV-09" or "NOV-27" => response.AnalysisRows?.Count == 2
            && response.AnalysisRows[0].Code == "STK-C"
            && response.AnalysisRows[1].Code == "STK-B",
        "NOV-15" or "NOV-35" or "NOV-50" or "NOV-59" => response.AnalysisRows?.Any(x => x.Code == "PRJ-001") == true,
        "NOV-17" or "NOV-37" or "NOV-52" => HasNavigation(response, "/warehouse/goods-receipts/new",
            "Mal Kabul → Operasyon → Emir Oluştur"),
        "NOV-18" or "NOV-40" or "NOV-51" => HasNavigation(response, "/warehouse/transfers/new",
            "Depo(Ambar) İşlemleri → Depolar Arası Transfer → Normal Transfer → Transfer Taslağı"),
        "NOV-19" or "NOV-38" or "NOV-53" => HasNavigation(response, "/warehouse/inventory-counts",
            "Depo(Ambar) İşlemleri → Depo Yönetimi → Sayım Yönetimi"),
        "NOV-36" or "NOV-54" => HasNavigation(response, "/warehouse/stock-movements",
            "Depo(Ambar) İşlemleri → Depo Yönetimi → Stok Hareketleri"),
        "NOV-39" => HasNavigation(response, "/warehouse/production/generator/projects",
            "Üretim ve Kalite → Jeneratör Üretim → Planlama → Jeneratör Projeleri"),
        "NOV-41" => HasNavigation(response, "/erp/stocks", "Entegrasyonlar → Stoklar"),
        "NOV-46" => response.AnalysisRows?.Count == 2
            && response.AnalysisRows[0].Code == "STK-A"
            && response.AnalysisRows[1].Code == "STK-B",
        "NOV-58" => response.AnalysisRows?.Count == 1
            && response.AnalysisRows.Any(x => x.WarehouseCode == 10 && x.PhysicalQuantity == 110),
        _ => false
    };

    private static bool HasAnalysisCodes(WarehouseAssistantChatResponse response, params string[] expected) =>
        expected.All(code => response.AnalysisRows?.Any(x => x.Code == code) == true);

    private static bool HasNavigation(WarehouseAssistantChatResponse response, string route, string breadcrumb) =>
        response.AnalysisRows?.Any(x => x.Route == route) == true
        && response.Answer.Contains(breadcrumb, StringComparison.Ordinal)
        && !response.Answer.Contains(route, StringComparison.OrdinalIgnoreCase);

    private static int TotalRows(WarehouseAssistantChatResponse response) =>
        (response.AnalysisRows?.Count ?? 0)
        + response.StockLocations.Count
        + response.Movements.Count
        + response.Tasks.Count
        + (response.SummaryMetrics?.Count ?? 0);

    private static string DescribeRows(WarehouseAssistantChatResponse response)
    {
        var analysis = response.AnalysisRows?.Take(6).Select(x => string.Join(':', new object?[]
        {
            x.Code, x.Status, x.UnitCode, x.PhysicalQuantity, x.AvailableQuantity,
            x.ReservedQuantity, x.PlannedQuantity, x.ActualQuantity, x.VarianceQuantity, x.Route
        }.Select(value => value?.ToString() ?? "-"))) ?? [];
        var stockLocations = response.StockLocations.Take(6).Select(x =>
            $"{x.StockCode}:{x.WarehouseCode}:{x.LocationCode}:{x.Quantity}:{x.AvailableQuantity}");
        var metrics = response.SummaryMetrics?.Take(6).Select(x => $"{x.Key}:{x.Value}:{x.Unit}") ?? [];
        return string.Join("; ", analysis.Concat(stockLocations).Concat(metrics));
    }

    private static WmsDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static WarehouseAssistantService CreateService(UnitOfWork unitOfWork) => new(
        unitOfWork,
        new LocalHybridWarehouseAssistantIntentResolver(
            new WarehouseAssistantIntentResolver(),
            Microsoft.Extensions.Options.Options.Create(new WarehouseAssistantOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalHybridWarehouseAssistantIntentResolver>.Instance),
        new NoopAuditWriter(),
        new FixedTimeProvider(Now),
        localizer: new ResourceLocalizer());

    private static async Task SeedAsync(WmsDbContext db)
    {
        db.Users.Add(new User
        {
            Id = 10,
            Username = "operator",
            Email = "operator@v3rii.test",
            PasswordHash = "x",
            Role = "User"
        });
        db.Warehouses.AddRange(
            Warehouse(30, "0", 10, "Ana Depo"),
            Warehouse(31, "0", 20, "Yedek Depo"),
            Warehouse(32, "0", 99, "Gizli Depo"),
            Warehouse(33, "OTHER", 77, "Başka Şube Deposu"));
        db.UserWarehouseAssignments.AddRange(
            new UserWarehouseAssignment { Id = 201, BranchCode = "0", UserId = 10, WarehouseId = 30 },
            new UserWarehouseAssignment { Id = 202, BranchCode = "0", UserId = 10, WarehouseId = 31 });

        db.Set<WarehouseLocation>().AddRange(
            Location(40, "0", 30, "A01/R01-G01", "Dolu Göz", 100, "AD"),
            Location(41, "0", 30, "A01/R01-G02", "Boş Göz", 100, "AD"),
            Location(42, "0", 30, "KRN-01", "Karantina", null, null, isQuarantine: true),
            Location(43, "0", 32, "SECRET-01", "Gizli Lokasyon", 1000, "AD"));

        db.Set<StockEntity>().AddRange(
            Stock(50, "STK-A", "A Rulmanı", "Hammadde"),
            Stock(51, "STK-B", "B Cıvatası", "Hammadde"),
            Stock(52, "STK-C", "C Contası", "YedekParça"));
        db.Set<WarehouseStockBalance>().AddRange(
            WarehouseBalance(60, 30, 50, 100, 20, 80),
            WarehouseBalance(61, 31, 50, 25, 5, 20),
            WarehouseBalance(62, 30, 51, 10, 8, 2),
            WarehouseBalance(63, 32, 50, 999, 0, 999));
        db.Set<LocationStockBalance>().AddRange(
            LocationBalance(70, 30, 40, 50, 70, 15, 55),
            LocationBalance(71, 30, 40, 51, 10, 8, 2),
            LocationBalance(72, 32, 43, 50, 999, 0, 999));

        db.Set<InventoryCountHeader>().AddRange(
            new InventoryCountHeader
            {
                Id = 80, BranchCode = "0", DocumentNo = "CNT-2026-001", WarehouseId = 30,
                Status = InventoryCountStatus.InProgress, Description = "Ana depo çevrim sayımı",
                TaskCount = 2, CompletedTaskCount = 1, LineCount = 2, CountedLineCount = 1,
                VarianceLineCount = 1, PlannedStartUtc = Now.UtcDateTime.AddDays(-1)
            },
            new InventoryCountHeader
            {
                Id = 81, BranchCode = "0", DocumentNo = "CNT-2026-CANCELLED", WarehouseId = 30,
                Status = InventoryCountStatus.Cancelled, Description = "İptal sayım",
                PlannedStartUtc = Now.UtcDateTime.AddDays(-2)
            });
        db.Set<InventoryCountLine>().Add(new InventoryCountLine
        {
            Id = 82, BranchCode = "0", HeaderId = 80, TaskId = 800, WarehouseId = 30,
            LocationId = 40, StockId = 50, UnitCode = "AD", SnapshotQuantity = 70,
            CountedQuantity = 65, VarianceQuantity = -5, VariancePercentage = -7.14m,
            Status = InventoryCountLineStatus.Variance, DifferenceReasonCode = "COUNT_MISMATCH",
            LastCountedAtUtc = Now.UtcDateTime.AddHours(-2)
        });

        db.Set<GeneratorProductionProject>().AddRange(
            Project(90, "0", "PRJ-001", "Ana Jeneratör", GeneratorProjectStatus.InProgress, 2),
            Project(91, "0", "PRJ-DONE", "Tamamlanan Jeneratör", GeneratorProjectStatus.Completed, 1),
            Project(92, "OTHER", "PRJ-SECRET", "Başka Şube Projesi", GeneratorProjectStatus.InProgress, 5));
        db.Set<GeneratorProductionRouteOperation>().AddRange(
            new GeneratorProductionRouteOperation
            {
                Id = 100, BranchCode = "0", RouteId = 1000, OperationCode = "OP-CUT",
                OperationName = "Kesim", Sequence = 1, DurationMinutes = 60
            },
            new GeneratorProductionRouteOperation
            {
                Id = 101, BranchCode = "0", RouteId = 1000, OperationCode = "OP-WELD",
                OperationName = "Kaynak", Sequence = 2, DurationMinutes = 120
            });
        db.Set<GeneratorProductionStation>().Add(new GeneratorProductionStation
        {
            Id = 110, BranchCode = "0", Code = "ST-WELD", Name = "Kaynak İstasyonu",
            Area = GeneratorStationArea.Stator, PlanningOrder = 1
        });
        db.Set<GeneratorProductionOperation>().AddRange(
            new GeneratorProductionOperation
            {
                Id = 120, BranchCode = "0", ProjectId = 90, RouteOperationId = 100, StationId = 110,
                UnitIndex = 1, Status = GeneratorOperationStatus.Completed,
                PlannedStartAtUtc = Now.UtcDateTime.AddDays(-3), PlannedEndAtUtc = Now.UtcDateTime.AddDays(-2),
                ActualStartAtUtc = Now.UtcDateTime.AddDays(-3), ActualEndAtUtc = Now.UtcDateTime.AddDays(-2), GoodQuantity = 1
            },
            new GeneratorProductionOperation
            {
                Id = 121, BranchCode = "0", ProjectId = 90, RouteOperationId = 101, StationId = 110,
                UnitIndex = 2, Status = GeneratorOperationStatus.InProgress, HasMaterialShortage = true,
                PlannedStartAtUtc = Now.UtcDateTime.AddDays(-2), PlannedEndAtUtc = Now.UtcDateTime.AddDays(-1)
            });
        db.Set<GeneratorProductionQualityGate>().Add(new GeneratorProductionQualityGate
        {
            Id = 130, BranchCode = "0", OperationId = 121,
            Status = GeneratorQualityGateStatus.Pending, RequestedAtUtc = Now.UtcDateTime.AddHours(-4)
        });

        await db.SaveChangesAsync();
    }

    private static WarehouseEntity Warehouse(long id, string branch, int code, string name) => new()
    {
        Id = id, BranchCode = branch, WarehouseCode = code, WarehouseName = name
    };

    private static WarehouseLocation Location(
        long id, string branch, long warehouseId, string code, string name,
        decimal? capacity, string? capacityUnit, bool isQuarantine = false) => new()
    {
        Id = id, BranchCode = branch, WarehouseId = warehouseId, Code = code, Name = name,
        CapacityQuantity = capacity, CapacityUnit = capacityUnit, IsQuarantine = isQuarantine,
        LocationType = isQuarantine ? LocationTypes.Quarantine : LocationTypes.Cell
    };

    private static StockEntity Stock(long id, string code, string name, string group) => new()
    {
        Id = id, BranchCode = "0", ErpStockCode = code, StockName = name,
        GroupCode = group, BaseUnitCode = "AD"
    };

    private static WarehouseStockBalance WarehouseBalance(
        long id, long warehouseId, long stockId, decimal physical, decimal reserved, decimal available) => new()
    {
        Id = id, BranchCode = "0", WarehouseId = warehouseId, StockId = stockId,
        DimensionKey = $"W-{id}", UnitCode = "AD", Quantity = physical,
        ReservedQuantity = reserved, AvailableQuantity = available, LastTransactionDate = Now.UtcDateTime
    };

    private static LocationStockBalance LocationBalance(
        long id, long warehouseId, long locationId, long stockId,
        decimal physical, decimal reserved, decimal available) => new()
    {
        Id = id, BranchCode = "0", WarehouseId = warehouseId, LocationId = locationId, StockId = stockId,
        DimensionKey = $"L-{id}", UnitCode = "AD", Quantity = physical,
        ReservedQuantity = reserved, AvailableQuantity = available, LastTransactionDate = Now.UtcDateTime
    };

    private static GeneratorProductionProject Project(
        long id, string branch, string code, string name, GeneratorProjectStatus status, int quantity) => new()
    {
        Id = id, BranchCode = branch, ProjectCode = code, ProjectName = name, Status = status,
        Quantity = quantity, Priority = 50,
        PlannedStartAtUtc = Now.UtcDateTime.AddDays(-5), PlannedDeliveryAtUtc = Now.UtcDateTime.AddDays(5)
    };

    private static readonly WarehouseAssistantAccess FullAccess = new(
        CanQueryAllUsers: true,
        CanViewStockBalances: true,
        CanViewStockMovements: true,
        CanViewGoodsReceipts: true,
        CanViewWarehouseTransfers: true,
        CanViewShipping: true,
        CanViewWarehouseInbound: true,
        CanViewWarehouseOutbound: true,
        CanViewProductionTransfers: true,
        CanViewSteelVehicles: true,
        CanViewQuality: true,
        CanViewPacking: true,
        CanViewProcurement: true,
        CanViewKkd: true,
        CanViewSystemHealth: true,
        CanViewLocations: true,
        CanViewInventoryCounts: true,
        CanReviewInventoryCounts: true,
        CanViewGeneratorProduction: true,
        CanViewErpMirror: true,
        CanCreateGoodsReceipts: true,
        CanCreateWarehouseTransfers: true,
        CanCreateInventoryCounts: true);

    private static IReadOnlyList<EvaluationCase> Cases =>
    [
        C("EXP-01", "İşi bilen", "Kaç depo var?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseCount),
        C("EXP-02", "İşi bilen", "10 numaralı depoda hangi lokasyonlar var?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseLocations),
        C("EXP-03", "İşi bilen", "10 depodaki toplam fiziksel, rezerve ve kullanılabilir stok nedir?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseStockTotals),
        C("EXP-04", "İşi bilen", "A01/R01-G01 lokasyonunda hangi ürünler var?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationContents),
        C("EXP-05", "İşi bilen", "A01/R01-G01 kapasitesi ve doluluğu nedir?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationCapacity),
        C("EXP-06", "İşi bilen", "Karantina lokasyonları hangileri?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationListByType),
        C("EXP-07", "İşi bilen", "Stoku olmayan ürünler hangileri?", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.ZeroStock),
        C("EXP-08", "İşi bilen", "En fazla stoklu 2 ürünü göster", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.RankedStock),
        C("EXP-09", "İşi bilen", "Hammadde grubundaki stokları karşılaştır", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.StockGroupComparison),
        C("EXP-10", "İşi bilen", "Kritik stok seviyesindeki ürünler hangileri?", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.CriticalStockUnsupported),
        C("EXP-11", "İşi bilen", "Açık sayımlar hangileri?", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountList),
        C("EXP-12", "İşi bilen", "Sayım farkı olan ürünler nelerdir?", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountVariance),
        C("EXP-13", "İşi bilen", "Aktif jeneratör üretim projeleri hangileri?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionProjects),
        C("EXP-14", "İşi bilen", "Hangi jeneratör üretimleri malzeme bekliyor?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionMaterialShortages),
        C("EXP-15", "İşi bilen", "Kalite kontrol bekleyen jeneratör üretimleri hangileri?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionQualityWaiting),
        C("EXP-16", "İşi bilen", "PRJ-001 için planlanan ve gerçekleşen jeneratör üretim miktarı nedir?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionPlannedVsActual),
        C("EXP-17", "İşi bilen", "Geciken jeneratör üretimler var mı?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionOverdue),
        C("EXP-18", "İşi bilen", "Stok hareketleri ekranı nerede?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("EXP-19", "İşi bilen", "99 depodaki toplam fiziksel stok nedir?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseStockTotals),
        C("EXP-20", "İşi bilen", "WT-2026-001 transferini onayla", WarehouseAssistantIntent.Unknown, WarehouseAssistantQueryKind.None),

        C("NOV-01", "İşi bilmeyen", "Bizim kaç tane ambar var?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseCount),
        C("NOV-02", "İşi bilmeyen", "10 numaralı ambardaki gözleri göster", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseLocations),
        C("NOV-03", "İşi bilmeyen", "Ürn STK-A nerde var?", WarehouseAssistantIntent.StockLocationBalance, WarehouseAssistantQueryKind.None),
        C("NOV-04", "İşi bilmeyen", "A01/R01-G01 rafında ne var?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationContents),
        C("NOV-05", "İşi bilmeyen", "A01/R01-G02 boşmu?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationEmptyCheck),
        C("NOV-06", "İşi bilmeyen", "A01/R01-G01 doluluk ne alemde?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationCapacity),
        C("NOV-07", "İşi bilmeyen", "Hiç kalmayan mallar hangileri?", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.ZeroStock),
        C("NOV-08", "İşi bilmeyen", "Depoda en çok hangi maldan var?", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.RankedStock),
        C("NOV-09", "İşi bilmeyen", "Kullanabileceğimiz en az 2 ürün ne?", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.RankedStock),
        C("NOV-10", "İşi bilmeyen", "Hammadde tarafındaki malları kıyasla", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.StockGroupComparison),
        C("NOV-11", "İşi bilmeyen", "Devam eden sayım işleri?", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountList),
        C("NOV-12", "İşi bilmeyen", "Sayımda tutmayan kalemler hangileri?", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountVariance),
        C("NOV-13", "İşi bilmeyen", "Jeneratörde parça bekleyen işler hangileri?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionMaterialShortages),
        C("NOV-14", "İşi bilmeyen", "Geciken jenaratör işleri var mı?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionOverdue),
        C("NOV-15", "İşi bilmeyen", "PRJ-001 ne alemde?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionProjectStatus),
        C("NOV-16", "İşi bilmeyen", "Riskli seviyeye düşen ürünler hangileri?", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.CriticalStockUnsupported),
        C("NOV-17", "İşi bilmeyen", "Yeni mal girişi nereden açılıyor?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-18", "İşi bilmeyen", "İki depo arasında ürün yollayacağım, nereden?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-19", "İşi bilmeyen", "Sayım sayfasını nereden bulurum?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-20", "İşi bilmeyen", "Şu transferi hemen onayla", WarehouseAssistantIntent.Unknown, WarehouseAssistantQueryKind.None),
        C("NOV-21", "İşi bilmeyen", "Ambarları sayar mısın?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseCount),
        C("NOV-22", "İşi bilmeyen", "Kaç adet depo mevcut?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseCount),
        C("NOV-23", "İşi bilmeyen", "10 ambarda hangi raf gözleri var?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseLocations),
        C("NOV-24", "İşi bilmeyen", "A01/R01-G02 göz boşta mı?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationEmptyCheck),
        C("NOV-25", "İşi bilmeyen", "Elde hiç olmayan ürünleri getir", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.ZeroStock),
        C("NOV-26", "İşi bilmeyen", "En çok bulunan 3 ürünü sırala", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.RankedStock),
        C("NOV-27", "İşi bilmeyen", "Kullanılabilir miktarı en düşük 2 malzeme", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.RankedStock),
        C("NOV-28", "İşi bilmeyen", "Hammadde ürünlerini yan yana göster", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.StockGroupComparison),
        C("NOV-29", "İşi bilmeyen", "Sayım işi açık kalanlar", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountList),
        C("NOV-30", "İşi bilmeyen", "Sayımda eksik fazla çıkanlar", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountVariance),
        C("NOV-31", "İşi bilmeyen", "İptal sayımları gösterme", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountList),
        C("NOV-32", "İşi bilmeyen", "Jenaratörde malzeme yüzünden duran işler", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionMaterialShortages),
        C("NOV-33", "İşi bilmeyen", "Kontrolden onay bekleyen jeneratörler", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionQualityWaiting),
        C("NOV-34", "İşi bilmeyen", "Jeneratörlerde kaç planladık kaç bitirdik?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionPlannedVsActual),
        C("NOV-35", "İşi bilmeyen", "PRJ-001 işi nasıl gidiyor?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionProjectStatus),
        C("NOV-36", "İşi bilmeyen", "Stokların giriş çıkışına nerden bakılır?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-37", "İşi bilmeyen", "Mal kabul sayfasını nereden açacağım?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-38", "İşi bilmeyen", "Yeni sayım başlatmak için hangi sayfa?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-39", "İşi bilmeyen", "Jeneratör projelerini hangi menüden bulurum?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-40", "İşi bilmeyen", "Depolar arası transfer ekranına nasıl giderim?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-41", "İşi bilmeyen", "Yeni ürün kartı nereden ekleniyor?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-42", "İşi bilmeyen", "10 nolu ambarın raflarını göster", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseLocations),
        C("NOV-43", "İşi bilmeyen", "A01/R01-G01 içinde hangi mallar duruyor?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationContents),
        C("NOV-44", "İşi bilmeyen", "A01/R01-G02 boşta mı dolu mu?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationEmptyCheck),
        C("NOV-45", "İşi bilmeyen", "Stoğu bitmiş ürünler neler?", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.ZeroStock),
        C("NOV-46", "İşi bilmeyen", "İlk 2 en yüksek stok hangisi?", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.RankedStock),
        C("NOV-47", "İşi bilmeyen", "Hammadde grubunu mukayese et", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.StockGroupComparison),
        C("NOV-48", "İşi bilmeyen", "Sayımda eksik çıkanları göster", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountVariance),
        C("NOV-49", "İşi bilmeyen", "Jeneratör işinde materyal bekleyenler", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionMaterialShortages),
        C("NOV-50", "İşi bilmeyen", "PRJ-001 hangi aşamada kaldı?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionProjectStatus),
        C("NOV-51", "İşi bilmeyen", "Transfer sayfası nerdeydi?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-52", "İşi bilmeyen", "Mal kabulü hangi menüden yapıyorum?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-53", "İşi bilmeyen", "Envanter sayma ekranı nerede?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-54", "İşi bilmeyen", "Ürünlerin hareketine nereden bakacağım?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation),
        C("NOV-55", "İşi bilmeyen", "A01/R01-G01 rafında ne var?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationContents, "location-follow-up"),
        C("NOV-56", "İşi bilmeyen", "Peki kapasitesi ne kadar?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationCapacity, "location-follow-up"),
        C("NOV-57", "İşi bilmeyen", "10 numaralı depoda hangi lokasyonlar var?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseLocations, "warehouse-follow-up"),
        C("NOV-58", "İşi bilmeyen", "Peki toplam stok ne kadar?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseStockTotals, "warehouse-follow-up"),
        C("NOV-59", "İşi bilmeyen", "PRJ-001 jeneratör projesi ne durumda?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionProjectStatus, "project-follow-up"),
        C("NOV-60", "İşi bilmeyen", "Peki parça bekleyen işi var mı?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionMaterialShortages, "project-follow-up"),
        C("NOV-61", "İşi bilmeyen", "Şu transferi onaylayıver", WarehouseAssistantIntent.Unknown, WarehouseAssistantQueryKind.None),
        C("NOV-62", "İşi bilmeyen", "Bu sayımı iptal edebilir misin?", WarehouseAssistantIntent.Unknown, WarehouseAssistantQueryKind.None),
        C("NOV-63", "İşi bilmeyen", "STK-A kaydını güncelleyebilir misin?", WarehouseAssistantIntent.Unknown, WarehouseAssistantQueryKind.None),
        C("NOV-64", "İşi bilmeyen", "Transferi onaylarmısın?", WarehouseAssistantIntent.Unknown, WarehouseAssistantQueryKind.None)
    ];

    private static EvaluationCase C(
        string id,
        string persona,
        string question,
        WarehouseAssistantIntent expectedIntent,
        WarehouseAssistantQueryKind expectedQueryKind,
        string? conversationKey = null) =>
        new(id, persona, question, expectedIntent, expectedQueryKind, conversationKey);

    private sealed record EvaluationCase(
        string Id,
        string Persona,
        string Question,
        WarehouseAssistantIntent ExpectedIntent,
        WarehouseAssistantQueryKind ExpectedQueryKind,
        string? ConversationKey);

    private sealed record EvaluationResult(
        string Id,
        string Persona,
        string Question,
        string ExpectedIntent,
        string ExpectedQueryKind,
        string ActualIntent,
        string ActualQueryKind,
        string Answer,
        string Scope,
        string ProviderMode,
        string Rows,
        bool IntentCorrect,
        bool PlanCorrect,
        bool DataCorrect,
        bool Correct);

    private sealed class NoopAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ResourceLocalizer : IStringLocalizer<WarehouseAssistantResource>
    {
        private static readonly ResourceManager Resources = new(
            "verii_wms_api_v2.Modules.WarehouseAssistant.Localization.WarehouseAssistantResource",
            typeof(WarehouseAssistantResource).Assembly);
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");

        public LocalizedString this[string name]
        {
            get
            {
                var value = Resources.GetString(name, Culture);
                return new LocalizedString(name, value ?? name, value is null);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var value = Resources.GetString(name, Culture);
                return new LocalizedString(
                    name,
                    value is null ? name : string.Format(Culture, value, arguments),
                    value is null);
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
