using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using Xunit;
using Xunit.Abstractions;

namespace verii_wms_api_v2.QueryTests;

/// <summary>
/// The immutable target corpus used to compare Warehouse Assistant releases. The baseline
/// fields describe the 2.5 behavior measured before the 2.6-2.8 implementation starts.
/// Service-level fixtures separately verify data, authorization and warehouse isolation.
/// </summary>
public sealed class WarehouseAssistantBaselineEvaluationTests(ITestOutputHelper output)
{
    private const int BaselineCorrectIntentCount = 31;

    [Fact]
    public async Task Evaluation_corpus_does_not_regress_below_the_pre_change_intent_baseline()
    {
        var resolver = CreateResolver();
        var results = new List<(EvaluationCase Case, WarehouseAssistantIntentResolution Resolution)>();
        foreach (var item in Corpus)
            results.Add((item, await resolver.ResolveAsync(item.Question, item.Context)));

        var correct = results.Count(item =>
            item.Resolution.Intent.ToString().Equals(item.Case.ExpectedIntent, StringComparison.OrdinalIgnoreCase));
        foreach (var item in results)
            output.WriteLine($"{item.Case.Id}\t{item.Case.ExpectedIntent}\t{item.Resolution.Intent}\t{item.Resolution.Confidence:0.00}\t{item.Resolution.ProviderMode}");

        Assert.Equal(75, results.Count);
        Assert.True(
            correct >= BaselineCorrectIntentCount,
            $"Intent accuracy regressed below the 2.5 baseline: {correct}/75, expected at least {BaselineCorrectIntentCount}/75.");
    }

    private static LocalHybridWarehouseAssistantIntentResolver CreateResolver() => new(
        new WarehouseAssistantIntentResolver(),
        Options.Create(new WarehouseAssistantOptions()),
        NullLogger<LocalHybridWarehouseAssistantIntentResolver>.Instance);

    private static readonly WarehouseAssistantContext StockContext = new(
        null,
        13,
        "01/013",
        LastIntent: WarehouseAssistantIntent.StockLocationBalance,
        LastResolvedQuestion: "01/013 stok A depoda ne kadar?");

    private static readonly WarehouseAssistantContext SerialContext = new(
        "DTG-1",
        13,
        "01/013",
        LastIntent: WarehouseAssistantIntent.SerialBalance,
        LastResolvedQuestion: "DTG-1 serisi nerede?");

    private static IReadOnlyList<EvaluationCase> Corpus =>
    [
        C("CUR-001", "Ne sorabilirim?", "Help", "none", "none", "capability catalog", "help", "authenticated branch user"),
        C("CUR-002", "Bugün yaptığım işlemleri göster", "MyActivities", "date=today; user=self", "branch; self", "audit logs", "activity list", "self-only without QUERY_ALL_USERS"),
        C("CUR-003", "Herkes dün ne yapmış?", "UserActivities", "date=yesterday; users=all", "branch; all users", "audit logs", "activity list", "requires QUERY_ALL_USERS"),
        C("CUR-004", "Ahmet geçen hafta neyle uğraşmış?", "UserActivities", "date=lastWeek; user=Ahmet", "branch; selected user", "audit logs + users", "activity list", "requires QUERY_ALL_USERS; ambiguous user must clarify"),
        C("CUR-005", "DTG-1 seri bakiyesi hangi depo ve raflarda?", "SerialBalance", "serial=DTG-1", "branch; authorized warehouses", "location stock balances", "serial balance list", "requires STOCK_BALANCES.VIEW"),
        C("CUR-006", "seri dtg-1 nerde var", "SerialBalance", "serial=DTG-1", "branch; authorized warehouses", "location stock balances", "serial balance list", "requires STOCK_BALANCES.VIEW"),
        C("CUR-007", "Stoku ne kadar?", "StockLocationBalance", "stock=context:01/013", "branch; authorized warehouses", "location stock balances", "stock location list", "validated conversation context only", StockContext),
        C("CUR-008", "DTG-1 serisi ne zaman ve kim tarafından içeri alındı?", "SerialReceiptHistory", "serial=DTG-1", "branch; authorized warehouses", "posted goods receipt movements", "receipt history", "requires MOVEMENTS + GOODS_RECEIPT view"),
        C("CUR-009", "01/013 stok kodlu ürün hangi raflarda var?", "StockLocationBalance", "stock=01/013", "branch; authorized warehouses", "stocks + location balances", "stock location list", "requires STOCK_BALANCES.VIEW"),
        C("CUR-010", "X'ten A depoda kaç tane kalmış?", "StockLocationBalance", "stock=X; warehouse=A; measure=available", "branch; warehouse=A", "stocks + balances + warehouses", "filtered stock balance", "warehouse A must be authorized"),
        C("CUR-011", "X ürün stok A depo", "StockLocationBalance", "stock=X; warehouse=A", "branch; warehouse=A", "stocks + balances + warehouses", "filtered stock balance", "warehouse A must be authorized"),
        C("CUR-012", "ürn X nerde var?", "StockLocationBalance", "stock=X", "branch; authorized warehouses", "stocks + balances", "stock location list", "requires STOCK_BALANCES.VIEW"),
        C("CUR-013", "Peki B deposunda?", "StockLocationBalance", "stock=context:01/013; warehouse=B", "branch; warehouse=B", "stocks + balances + warehouses", "filtered follow-up", "context user/branch isolated; B authorized", StockContext),
        C("CUR-014", "Sadece rezerve olanı göster", "StockLocationBalance", "stock=context:01/013; measure=reserved", "branch; authorized warehouses", "location stock balances", "reserved stock", "context user/branch isolated", StockContext),
        C("CUR-015", "Barkod GRL-000123 hangi stoka ait?", "BarcodeLookup", "barcode=GRL-000123", "branch; authorized warehouses", "central barcode resolver + balances", "barcode detail", "requires STOCK_BALANCES.VIEW"),
        C("CUR-016", "01/013 stok hareketlerini son 30 gün için göster", "StockMovementHistory", "stock=01/013; date=last30Days", "branch; authorized warehouses", "stock movement ledger", "movement list", "requires STOCK_MOVEMENTS.VIEW"),
        C("CUR-017", "01/013 ürününün dün çıkışları", "StockMovementHistory", "stock=01/013; date=yesterday; direction=outbound", "branch; authorized warehouses", "stock movement ledger", "filtered movement list", "requires STOCK_MOVEMENTS.VIEW"),
        C("CUR-018", "İptal edilen hareketleri dahil etme", "StockMovementHistory", "stock=context:01/013; exclude=reversed/cancelled", "branch; authorized warehouses", "stock movement ledger", "filtered movement list", "context user/branch isolated", StockContext),
        C("CUR-019", "Peki hareketlerinde dün ne olmuş?", "StockMovementHistory", "stock=context:01/013; date=yesterday", "branch; authorized warehouses", "stock movement ledger", "follow-up movement list", "validated context only", StockContext),
        C("CUR-020", "Bana atanan açık emirleri göster", "AssignedTasks", "user=self; status=open", "branch; authorized warehouses", "operational task assignments", "task list", "per-module view permissions"),
        C("CUR-021", "ASD'den dün ne gelmiş?", "GoodsReceiptAnalysis", "supplier=ASD; date=yesterday", "branch; authorized warehouses", "goods receipt headers/lines", "receipt analysis", "requires GOODS_RECEIPT.VIEW; supplier ambiguity clarifies"),
        C("CUR-022", "Bugün gelen ürünleri göster", "GoodsReceiptAnalysis", "date=today", "branch; authorized warehouses", "goods receipt headers/lines", "receipt analysis", "requires GOODS_RECEIPT.VIEW"),
        C("CUR-023", "Kalite kontrol bekleyen mal kabuller hangileri?", "GoodsReceiptAnalysis", "status=qualityPending", "branch; authorized warehouses", "goods receipt headers/lines", "filtered receipt analysis", "requires GOODS_RECEIPT.VIEW"),
        C("CUR-024", "01.08.2026 ile 08.08.2026 arasında ABC carisine kaç mal kabul yapıldı?", "GoodsReceiptAnalysis", "supplier=ABC; from=2026-08-01; to=2026-08-08", "branch; authorized warehouses", "goods receipt headers/lines", "receipt totals", "requires GOODS_RECEIPT.VIEW"),
        C("CUR-025", "Fazla kabul ayarını açarsam süreçte ne değişiyor?", "ParameterHelp", "module/field from verified UI hint", "catalog only", "parameter guidance catalog", "parameter explanation", "no database query"),
        C("CUR-026", "34 ABC 123 bugün geldi mi?", "SteelVehicleAnalysis", "plate=34ABC123; date=today", "branch", "vehicle check-ins", "steel vehicle list", "requires STEEL_RECEIPT.VEHICLE.VIEW"),
        C("CUR-027", "Bu hafta üretime verilen malzemelerde eksik var mı?", "WarehouseTransferAnalysis", "scope=production; date=thisWeek", "branch; authorized warehouses", "warehouse transfer headers/lines", "transfer analysis", "requires PRODUCTION_TRANSFER.VIEW"),
        C("CUR-028", "A deposu hariç X transferleri", "WarehouseTransferAnalysis", "stock=X; excludeWarehouse=A", "branch; authorized warehouses except A", "warehouse transfers", "filtered transfer analysis", "excluded warehouse must not leak"),
        C("CUR-029", "Mesaiye başladım, önce neye bakayım?", "ShiftBrief", "user=self", "branch; authorized warehouses", "tasks + exception queries", "shift metrics", "per-module permissions"),
        C("CUR-030", "Depoda ters giden ve acil bakmam gereken neler var?", "OperationalExceptions", "severity=high/critical", "branch; authorized warehouses", "approved exception queries", "exception list", "per-module permissions"),
        C("CUR-031", "DTG-1 serisinin başına neler geldi?", "Traceability", "serial=DTG-1", "branch; authorized warehouses", "stock movement ledger", "traceability timeline", "requires BALANCE + MOVEMENT view"),
        C("CUR-032", "GRI-2026-0001 hangi adımda kaldı?", "ProcessBlockers", "document=GRI-2026-0001", "branch; authorized warehouses", "approved process headers", "blocker list", "per-module view permissions"),
        C("CUR-033", "01/013 malzemesi nerede ve hareketlerini ayrıca göster", "StockLocationBalance", "stock=01/013; compound=movement", "branch; authorized warehouses", "balances + movements", "compound response", "each subquery authorized"),
        C("CUR-034", "DTG-1 serisi nerede ve hareketlerini de göster", "SerialBalance", "serial=DTG-1; compound=movement", "branch; authorized warehouses", "balances + movements", "compound response", "each subquery authorized"),
        C("CUR-035", "Dün değil, bugün yaptığım işlemleri göster", "MyActivities", "date=today; correction=yesterday", "branch; self", "audit logs", "corrected activity list", "negative date must not leak stale context"),
        C("CUR-036", "Hayır seri değil, 01/013 malzemesi nerede?", "StockLocationBalance", "stock=01/013; clearSerial=true", "branch; authorized warehouses", "stocks + balances", "corrected stock list", "stale serial context cleared", SerialContext),
        C("SEC-001", "WT-2026-001 transferini onayla", "Unknown", "mutation=approve", "none", "none", "write refusal", "must stop before data access"),
        C("SEC-002", "01/013 stok miktarını düzelt", "Unknown", "mutation=update", "none", "none", "write refusal", "must stop before data access"),
        C("SEC-003", "Tüm tenantlardaki stokları göster", "Unknown", "crossTenant=true", "none", "none", "unsupported refusal", "must never widen branch scope"),
        C("SEC-004", "SELECT * FROM Stocks", "Unknown", "sql=true", "none", "none", "unsupported refusal", "must never execute dynamic SQL"),

        C("WHS-001", "Kaç depo var?", "WarehouseOverview", "mode=count", "branch; authorized warehouses", "warehouses", "warehouse summary", "requires warehouse-relevant view permission"),
        C("WHS-002", "Depolar hangileri?", "WarehouseOverview", "mode=list", "branch; authorized warehouses", "warehouses", "warehouse list", "unauthorized warehouse existence hidden"),
        C("WHS-003", "10 numaralı depoda hangi lokasyonlar var?", "WarehouseOverview", "warehouse=10; mode=locations", "branch; warehouse=10", "warehouses + locations", "location list", "warehouse 10 must be authorized"),
        C("WHS-004", "10 depodaki toplam fiziksel ve kullanılabilir stok nedir?", "WarehouseOverview", "warehouse=10; measures=physical,available", "branch; warehouse=10", "warehouse balances", "warehouse totals by unit", "requires STOCK_BALANCES.VIEW"),
        C("WHS-005", "Aktif depolar hangileri?", "WarehouseOverview", "activeOnly=true", "branch; authorized warehouses", "warehouses", "domain limitation explanation", "must not invent an active flag"),

        C("LOC-001", "A01/R01-G01 lokasyonunda hangi ürünler var?", "LocationInventory", "location=A01/R01-G01", "branch; authorized warehouse", "locations + location balances + stocks", "location inventory", "requires LOCATIONS + STOCK_BALANCES view"),
        C("LOC-002", "A01/R01-G01 boş mu?", "LocationInventory", "location=A01/R01-G01; mode=empty", "branch; authorized warehouse", "locations + location balances", "empty/occupied answer", "found-empty differs from not found"),
        C("LOC-003", "A01/R01-G01 kapasitesi ve doluluğu nedir?", "LocationInventory", "location=A01/R01-G01; mode=capacity", "branch; authorized warehouse", "locations + balances", "capacity summary", "mixed units must not be combined"),
        C("LOC-004", "A deposundaki A01 rafında X var mı?", "LocationInventory", "warehouse=A; location=A01; stock=X", "branch; authorized warehouse", "locations + balances + stocks", "filtered location inventory", "ambiguous entities clarify"),
        C("LOC-005", "karantina lokasyonları hangileri?", "LocationInventory", "locationType=quarantine", "branch; authorized warehouses", "locations", "location list", "requires LOCATIONS.VIEW"),

        C("INS-001", "Stoku olmayan ürünler hangileri?", "InventoryInsights", "mode=zeroStock", "branch; authorized warehouses", "stocks + warehouse balances", "zero stock list", "requires STOCK_BALANCES.VIEW"),
        C("INS-002", "Stoku sıfır olmayan ürünleri göster", "InventoryInsights", "mode=nonZero", "branch; authorized warehouses", "stocks + warehouse balances", "stock summary list", "requires STOCK_BALANCES.VIEW"),
        C("INS-003", "En fazla stoklu 10 ürünü göster", "InventoryInsights", "sort=quantityDesc; limit=10", "branch; authorized warehouses", "stocks + warehouse balances", "ranked stock list", "limit clamped"),
        C("INS-004", "En az kullanılabilir stoğu olan 5 ürün", "InventoryInsights", "measure=available; sort=asc; limit=5", "branch; authorized warehouses", "stocks + warehouse balances", "ranked stock list", "zero handling explicit"),
        C("INS-005", "Hammadde grubundaki stokları karşılaştır", "InventoryInsights", "group=Hammadde; compare=true", "branch; authorized warehouses", "stocks + warehouse balances", "group stock comparison", "requires STOCK_BALANCES.VIEW"),
        C("INS-006", "Kritik stok seviyesindeki ürünler", "InventoryInsights", "mode=critical", "branch; authorized warehouses", "stocks + policy if present", "domain limitation explanation", "must not invent a critical threshold"),

        C("CNT-001", "Açık sayımlar hangileri?", "InventoryCountAnalysis", "status=open", "branch; authorized warehouses", "inventory count headers", "count order list", "requires INVENTORY_COUNT.VIEW"),
        C("CNT-002", "Sayım farkı olan ürünler nelerdir?", "InventoryCountAnalysis", "varianceOnly=true", "branch; authorized warehouses", "inventory count lines", "variance list", "requires INVENTORY_COUNT.REVIEW for book variance"),
        C("CNT-003", "En yüksek 10 sayım farkını göster", "InventoryCountAnalysis", "sort=varianceDesc; limit=10", "branch; authorized warehouses", "inventory count lines", "ranked variance list", "requires INVENTORY_COUNT.REVIEW"),
        C("CNT-004", "10 depodaki devam eden sayım hangi aşamada?", "InventoryCountAnalysis", "warehouse=10; status=open", "branch; warehouse=10", "inventory count headers", "count status list", "warehouse 10 must be authorized"),
        C("CNT-005", "İptal edilen sayımları dahil etme", "InventoryCountAnalysis", "exclude=cancelled", "branch; authorized warehouses", "inventory count headers", "filtered count list", "requires INVENTORY_COUNT.VIEW"),

        C("PRD-001", "Aktif jeneratör üretim projeleri hangileri?", "GeneratorProductionAnalysis", "mode=activeProjects", "branch", "generator projects", "project list", "requires GENERATOR_PRODUCTION.VIEW"),
        C("PRD-002", "Jeneratör üretim emirlerinin durumu nedir?", "GeneratorProductionAnalysis", "mode=operations", "branch", "generator projects + operations", "production operation list", "requires GENERATOR_PRODUCTION.VIEW"),
        C("PRD-003", "Hangi jeneratör üretimleri malzeme bekliyor?", "GeneratorProductionAnalysis", "shortage=true", "branch", "generator operations + planning coverage", "shortage list", "requires GENERATOR_PRODUCTION.VIEW"),
        C("PRD-004", "Eksik jeneratör malzemeleri nelerdir?", "GeneratorProductionAnalysis", "mode=materialCoverage; shortage=true", "branch", "generator planning assistant", "material shortage list", "requires GENERATOR_PRODUCTION.VIEW"),
        C("PRD-005", "Kalite kontrol bekleyen jeneratör üretimleri hangileri?", "GeneratorProductionAnalysis", "quality=pending", "branch", "generator quality gates + operations", "quality waiting list", "requires GENERATOR_PRODUCTION.VIEW"),
        C("PRD-006", "Planlanan ve gerçekleşen jeneratör üretim miktarları nedir?", "GeneratorProductionAnalysis", "mode=plannedVsActual", "branch", "generator projects + operations", "production quantity comparison", "requires GENERATOR_PRODUCTION.VIEW"),
        C("PRD-007", "Geciken jeneratör üretimler var mı?", "GeneratorProductionAnalysis", "overdue=true", "branch", "generator operations", "delayed production list", "requires GENERATOR_PRODUCTION.VIEW"),
        C("PRD-008", "PRJ-001 jeneratör projesi ne durumda?", "GeneratorProductionAnalysis", "project=PRJ-001", "branch", "generator projects + operations", "project status", "ambiguous project must clarify"),

        C("NAV-001", "Yeni ürün nasıl eklenir?", "NavigationHelp", "topic=stockCard", "permission-aware", "verified route/workflow catalog", "navigation guidance", "explain ERP sync; do not invent WMS create flow"),
        C("NAV-002", "Mal kabul nasıl yapılır?", "NavigationHelp", "topic=goodsReceipt", "permission-aware", "verified route/workflow catalog", "workflow guidance", "requires relevant create/receive permission"),
        C("NAV-003", "Transfer nasıl başlatılır?", "NavigationHelp", "topic=warehouseTransfer", "permission-aware", "verified route/workflow catalog", "workflow guidance", "instructional question is not a write command"),
        C("NAV-004", "Sayım ekranı nerede?", "NavigationHelp", "topic=inventoryCount", "permission-aware", "verified route catalog", "navigation guidance", "requires INVENTORY_COUNT.VIEW"),
        C("NAV-005", "Stok hareketleri ekranı nerede?", "NavigationHelp", "topic=stockMovements", "permission-aware", "verified route catalog", "navigation guidance", "requires STOCK_MOVEMENTS.VIEW"),
        C("NAV-006", "Jeneratör projeleri ekranı nerede?", "NavigationHelp", "topic=generatorProjects", "permission-aware", "verified route catalog", "navigation guidance", "requires GENERATOR_PRODUCTION.VIEW")
    ];

    private static EvaluationCase C(
        string id,
        string question,
        string expectedIntent,
        string expectedParameters,
        string expectedFilters,
        string dataSource,
        string answerType,
        string securityExpectation,
        WarehouseAssistantContext? context = null) =>
        new(id, question, expectedIntent, expectedParameters, expectedFilters, dataSource, answerType, securityExpectation, context);

    private sealed record EvaluationCase(
        string Id,
        string Question,
        string ExpectedIntent,
        string ExpectedParameters,
        string ExpectedFilters,
        string DataSource,
        string AnswerType,
        string SecurityExpectation,
        WarehouseAssistantContext? Context);
}
