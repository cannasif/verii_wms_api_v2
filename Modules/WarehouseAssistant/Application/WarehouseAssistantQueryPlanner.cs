using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

internal sealed record WarehouseAssistantPlannedQuery(
    WarehouseAssistantIntent Intent,
    WarehouseAssistantQueryKind QueryKind,
    decimal Confidence,
    string? WarehouseQuery = null,
    string? LocationQuery = null,
    string? StockGroupQuery = null,
    string? ProjectQuery = null,
    string? StatusQuery = null,
    WarehouseAssistantStockMeasure? StockMeasure = null,
    WarehouseAssistantSortDirection Sort = WarehouseAssistantSortDirection.None,
    int? Limit = null,
    bool ExcludeZero = false,
    bool ExcludeCancelled = false,
    bool ActiveOnly = false,
    string? NavigationTopic = null,
    IReadOnlyList<string>? ReasonCodes = null);

/// <summary>
/// Converts supported analytical questions into a closed, typed plan. It never produces SQL,
/// widens authorization scope or resolves an entity; those remain service responsibilities.
/// </summary>
internal static partial class WarehouseAssistantQueryPlanner
{
    public static WarehouseAssistantPlannedQuery? TryPlan(
        string originalMessage,
        string normalizedMessage,
        WarehouseAssistantContext? context)
    {
        var question = new LocalWarehouseQuestion(normalizedMessage);

        var navigationTopic = ResolveNavigationTopic(question);
        if (navigationTopic is not null && question.HasAny(WarehouseAssistantTerminology.InstructionWords))
            return Plan(WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation, 0.99m,
                navigationTopic: navigationTopic, reasons: ["instructional-question", $"topic:{navigationTopic}"]);

        var contextualProjectQuestion = !string.IsNullOrWhiteSpace(context?.ProjectQuery)
            && question.HasAny(
                "eksik", "malzeme", "materyal", "parca bekleyen", "kalite", "kontrol",
                "geciken", "planlanan", "bitirdik", "urettik", "operasyon", "is");
        if (question.HasAny(WarehouseAssistantTerminology.GeneratorProductionWords)
            || ProjectRegex().IsMatch(normalizedMessage)
            || contextualProjectQuestion)
            return PlanGeneratorProduction(question, normalizedMessage, context);

        if (question.HasAny(WarehouseAssistantTerminology.InventoryCountWords))
            return PlanInventoryCount(question, normalizedMessage, context);

        // Serial balance questions may legitimately mention both a warehouse and a location.
        // Leave those established intents to the base resolver instead of treating them as
        // a request to list every location in the warehouse.
        if (question.HasAny("seri", "seri no", "serino", "serial")
            && question.HasAny("bakiye", "miktar", "nerede", "hangi depo", "hangi raf", "lokasyon"))
            return null;

        var warehousePlan = TryPlanWarehouse(question, normalizedMessage, context);
        if (warehousePlan is not null) return warehousePlan;

        var locationPlan = TryPlanLocation(question, normalizedMessage, context);
        if (locationPlan is not null) return locationPlan;

        if (question.HasAny(WarehouseAssistantTerminology.StockInsightWords)
            || question.HasAny("stoklari karsilastir", "stok seviyesindeki"))
            return PlanInventoryInsight(question, normalizedMessage);

        return null;
    }

    private static WarehouseAssistantPlannedQuery? TryPlanWarehouse(
        LocalWarehouseQuestion question,
        string normalized,
        WarehouseAssistantContext? context)
    {
        var hasWarehouseSignal = question.HasAny(WarehouseAssistantTerminology.WarehouseWords);
        var hasContextualWarehouseRequest = !string.IsNullOrWhiteSpace(context?.WarehouseQuery)
            && question.HasAny("toplam stok", "stok toplami", "lokasyon", "raf", "goz");
        if (!hasWarehouseSignal && !hasContextualWarehouseRequest) return null;

        var warehouse = ExtractWarehouseQuery(normalized) ?? context?.WarehouseQuery;
        if (ContainsAnyPhrase(normalized,
                "kac depo", "kac tane depo", "kac adet depo", "depo sayisi", "depo mevcut",
                "kac ambar", "kac tane ambar", "kac adet ambar", "ambar sayisi", "how many warehouses")
            || question.HasAny("depolari say", "ambarlari say"))
            return Plan(WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseCount, 0.99m,
                warehouse: warehouse, reasons: ["warehouse-signal", "aggregate:count"]);

        if (question.HasAny(
                "depolar hangileri", "depolari listele", "aktif depolar",
                "ambarlar hangileri", "ambarlari listele", "aktif ambarlar", "warehouse list"))
            return Plan(WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseList, 0.99m,
                warehouse: warehouse, activeOnly: question.HasAny("aktif"), reasons: ["warehouse-signal", "list-request"]);

        if (question.HasAny(WarehouseAssistantTerminology.LocationWords)
            && question.HasAny("hangi", "liste", "var", "goster", "say"))
            return Plan(WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseLocations, 0.98m,
                warehouse: warehouse, reasons: ["warehouse-signal", "warehouse-location-list"]);

        if (question.HasAny(
                "toplam fiziksel", "toplam kullanilabilir", "depo toplami", "ambar toplami",
                "stok toplami", "toplam stok"))
            return Plan(WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseStockTotals, 0.98m,
                warehouse: warehouse,
                measure: question.HasAny("kullanilabilir") ? WarehouseAssistantStockMeasure.Available : WarehouseAssistantStockMeasure.Physical,
                reasons: ["warehouse-signal", "aggregate:stock-total"]);

        return null;
    }

    private static WarehouseAssistantPlannedQuery? TryPlanLocation(
        LocalWarehouseQuestion question,
        string normalized,
        WarehouseAssistantContext? context)
    {
        var hasExplicitLocation = LocationBeforeRegex().IsMatch(normalized);
        var hasLocationCodeWithLocationQuestion = LocationCodeRegex().IsMatch(normalized)
            && question.HasAny(
                "bos mu", "bosta mi", "dolu mu", "kapasite", "doluluk", "lokasyon", "raf", "goz",
                "icinde", "hangi mal", "mallar duruyor");
        var hasContextualLocationRequest = !string.IsNullOrWhiteSpace(context?.LocationQuery)
            && question.HasAny("bos mu", "bosta mi", "dolu mu", "kapasite", "doluluk", "ne var", "icinde");
        if (!question.HasAny(WarehouseAssistantTerminology.LocationWords)
            && !hasLocationCodeWithLocationQuestion
            && !hasExplicitLocation
            && !hasContextualLocationRequest)
            return null;

        var warehouse = ExtractWarehouseQuery(normalized) ?? context?.WarehouseQuery;
        var location = ExtractLocationQuery(normalized) ?? context?.LocationQuery;
        if (question.HasAny("karantina lokasyon", "quarantine location"))
            return Plan(WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationListByType, 0.99m,
                warehouse: warehouse, location: location, status: "Quarantine", reasons: ["location-signal", "location-type:quarantine"]);

        if (question.HasAny("kapasite", "doluluk", "ne kadar dolu", "capacity", "occupancy"))
            return Plan(WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationCapacity, 0.99m,
                warehouse: warehouse, location: location, reasons: ["location-signal", "capacity-request"]);

        if (question.HasAny("bos mu", "bosta mi", "bos olan", "dolu mu", "empty"))
            return Plan(WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationEmptyCheck, 0.99m,
                warehouse: warehouse, location: location, reasons: ["location-signal", "occupancy-request"]);

        if (location is not null && question.HasAny(
                "hangi urun", "hangi mal", "urunler var", "mallar duruyor", "ne var", "icinde ne", "var mi",
                "lokasyonunda", "rafinda", "gozunde"))
            return Plan(WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationContents, 0.97m,
                warehouse: warehouse, location: location, reasons: ["location-signal", "location-contents"]);

        return null;
    }

    private static WarehouseAssistantPlannedQuery PlanInventoryInsight(LocalWarehouseQuestion question, string normalized)
    {
        var limit = ExtractLimit(normalized);
        if (question.HasAny("kritik stok", "riskli seviye", "asgari stok", "minimum stok"))
            return Plan(WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.CriticalStockUnsupported, 0.99m,
                reasons: ["inventory-insight", "domain-limitation:critical-threshold"]);

        if (question.HasAny("sifir olmayan"))
            return Plan(WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.NonZeroStock, 0.99m,
                excludeZero: true, reasons: ["inventory-insight", "filter:non-zero"]);

        if (question.HasAny(
                "stoku olmayan", "stok olmayan", "stoku sifir", "sifir stok", "hic kalmayan",
                "hic olmayan urun", "elde hic olmayan", "stokta hic olmayan", "mevcudu olmayan", "stogu bitmis"))
            return Plan(WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.ZeroStock, 0.99m,
                reasons: ["inventory-insight", "filter:zero"]);

        var group = ExtractGroupQuery(normalized);
        if (group is not null || question.HasAny(
                "stoklari karsilastir", "mallari kiyasla", "kiyasla", "mukayese", "yan yana goster"))
            return Plan(WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.StockGroupComparison, 0.97m,
                stockGroup: group, reasons: ["inventory-insight", "group-comparison"]);

        var ascending = question.HasAny("en az", "en dusuk", "lowest");
        return Plan(WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.RankedStock, 0.98m,
            measure: question.HasAny("kullanilabilir", "kullanabilecegimiz", "kullanabilecegim", "kullanima hazir")
                ? WarehouseAssistantStockMeasure.Available
                : WarehouseAssistantStockMeasure.Physical,
            sort: ascending ? WarehouseAssistantSortDirection.QuantityAscending : WarehouseAssistantSortDirection.QuantityDescending,
            limit: limit ?? 10,
            reasons: ["inventory-insight", ascending ? "sort:quantity-asc" : "sort:quantity-desc"]);
    }

    private static WarehouseAssistantPlannedQuery PlanInventoryCount(
        LocalWarehouseQuestion question,
        string normalized,
        WarehouseAssistantContext? context)
    {
        var warehouse = ExtractWarehouseQuery(normalized) ?? context?.WarehouseQuery;
        var excludeCancelled = question.HasAny(
            "iptal edilen", "iptalleri dahil etme", "iptal sayimlari gosterme", "iptal sayimlari getirme", "haric");
        var variance = question.HasAny(
            "fark", "varyans", "tutmayan", "uyusmayan", "eksik fazla", "eksik cikan", "fazla cikan", "sapma", "variance");
        return Plan(
            WarehouseAssistantIntent.InventoryCountAnalysis,
            variance ? WarehouseAssistantQueryKind.InventoryCountVariance : WarehouseAssistantQueryKind.InventoryCountList,
            0.99m,
            warehouse: warehouse,
            status: question.HasAny("acik", "devam eden") ? "Open" : null,
            sort: question.HasAny("en yuksek") ? WarehouseAssistantSortDirection.VarianceDescending : WarehouseAssistantSortDirection.None,
            limit: ExtractLimit(normalized),
            excludeCancelled: excludeCancelled,
            reasons: ["inventory-count-signal", variance ? "variance-request" : "count-list"]);
    }

    private static WarehouseAssistantPlannedQuery PlanGeneratorProduction(
        LocalWarehouseQuestion question,
        string normalized,
        WarehouseAssistantContext? context)
    {
        var project = ExtractProjectQuery(normalized) ?? context?.ProjectQuery;
        if (project is not null && question.HasAny(
                "ne durumda", "durumu", "ne alemde", "nasil gidiyor", "hangi asamada", "status"))
            return Plan(WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionProjectStatus, 0.99m,
                project: project, reasons: ["generator-production-signal", "project-status"]);
        if (question.HasAny(
                "eksik", "malzeme bekliyor", "materyal bekleyen", "parca bekleyen", "malzeme yuzunden",
                "malzeme yok", "materyal yok", "parca yok", "material shortage"))
            return Plan(WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionMaterialShortages, 0.99m,
                project: project, status: "MaterialShortage", reasons: ["generator-production-signal", "material-shortage"]);
        if (question.HasAny(
                "kalite kontrol bekleyen", "kontrolden onay bekleyen", "kontrol onayi bekleyen",
                "kalitede bekleyen", "quality pending"))
            return Plan(WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionQualityWaiting, 0.99m,
                project: project, status: "QualityPending", reasons: ["generator-production-signal", "quality-pending"]);
        if (question.HasAny(
                "planlanan ve gerceklesen", "kac planladik kac bitirdik", "kac planlandi kac bitti",
                "kac planladik kac urettik", "planlanan tamamlanan", "planned vs actual"))
            return Plan(WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionPlannedVsActual, 0.99m,
                project: project, reasons: ["generator-production-signal", "planned-vs-actual"]);
        if (question.HasAny("geciken", "overdue"))
            return Plan(WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionOverdue, 0.99m,
                project: project, status: "Overdue", reasons: ["generator-production-signal", "overdue"]);
        if (question.HasAny("uretim emir", "operasyon", "operations"))
            return Plan(WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionOperations, 0.98m,
                project: project, reasons: ["generator-production-signal", "operation-list"]);

        return Plan(WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionProjects, 0.97m,
            project: project, status: question.HasAny("aktif") ? "Active" : null,
            reasons: ["generator-production-signal", "project-list"]);
    }

    private static string? ResolveNavigationTopic(LocalWarehouseQuestion question)
    {
        if (question.HasAny("yeni urun", "urun kart", "stok kart", "urun ekle")) return "stockCard";
        if (question.HasAny("mal kabul", "mal giris", "urun giris", "depoya mal alma")) return "goodsReceipt";
        if (question.HasAny("transfer", "depolar arasi", "depo arasinda", "urun yolla", "mal yolla")) return "warehouseTransfer";
        if (question.HasAny("sayim", "envanter sayimi", "envanter sayma")) return "inventoryCount";
        if (question.HasAny(
                "stok hareket", "urun hareket", "stok giris cikis", "stoklarin giris cikis", "giris cikisina"))
            return "stockMovements";
        if (question.HasAny(WarehouseAssistantTerminology.GeneratorProductionWords)) return "generatorProjects";
        return null;
    }

    internal static string? ExtractWarehouseQuery(string normalized)
    {
        var before = WarehouseBeforeRegex().Match(normalized);
        if (before.Success && !IsEntityStopWord(before.Groups[1].Value)) return before.Groups[1].Value;
        var numbered = NumberedWarehouseRegex().Match(normalized);
        return numbered.Success ? numbered.Groups[1].Value : null;
    }

    private static string? ExtractLocationQuery(string normalized)
    {
        var match = LocationBeforeRegex().Match(normalized);
        if (match.Success && !IsEntityStopWord(match.Groups[1].Value)) return match.Groups[1].Value;
        var code = LocationCodeRegex().Match(normalized);
        return code.Success ? code.Value : null;
    }

    private static string? ExtractProjectQuery(string normalized)
    {
        var match = ProjectRegex().Match(normalized);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static string? ExtractGroupQuery(string normalized)
    {
        var match = StockGroupRegex().Match(normalized);
        if (match.Success) return match.Groups[1].Value.Trim();
        var colloquial = ColloquialStockGroupRegex().Match(normalized);
        if (colloquial.Success) return colloquial.Groups[1].Value.Trim();
        var comparison = GroupComparisonRegex().Match(normalized);
        return comparison.Success ? comparison.Groups[1].Value.Trim() : null;
    }

    private static int? ExtractLimit(string normalized)
    {
        var match = LimitRegex().Match(normalized);
        if (!match.Success) match = FlexibleLimitRegex().Match(normalized);
        if (!match.Success) match = FirstLimitRegex().Match(normalized);
        if (!match.Success) match = ItemLimitRegex().Match(normalized);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var value)) return null;
        return Math.Clamp(value, 1, 50);
    }

    private static bool IsEntityStopWord(string value) => value is
        "kac" or "hangi" or "aktif" or "toplam" or "tum" or "butun" or "tane" or "adet" or "bizim";

    internal static WarehouseAssistantStockMeasure? ExtractStockMeasure(string normalized)
    {
        var question = new LocalWarehouseQuestion(normalized);
        if (question.HasAny("rezerve", "reserved")) return WarehouseAssistantStockMeasure.Reserved;
        if (question.HasAny(
                "kullanilabilir", "kullanabilecegimiz", "kullanabilecegim", "kullanima hazir", "available"))
            return WarehouseAssistantStockMeasure.Available;
        if (question.HasAny("fiziksel", "physical")) return WarehouseAssistantStockMeasure.Physical;
        return null;
    }

    internal static string? ExtractMovementDirection(string normalized)
    {
        var question = new LocalWarehouseQuestion(normalized);
        if (question.HasAny("cikis", "outbound")) return "Outbound";
        if (question.HasAny("giris", "inbound")) return "Inbound";
        return null;
    }

    internal static bool ExtractExcludeCancelled(string normalized) =>
        new LocalWarehouseQuestion(normalized).HasAny(
            "iptal edilen", "iptalleri dahil etme", "iptal dahil etme",
            "iptal sayimlari gosterme", "iptal sayimlari getirme", "haric");

    private static bool ContainsAnyPhrase(string normalized, params string[] phrases) =>
        phrases.Any(phrase => normalized.Contains(WarehouseAssistantTextNormalizer.Normalize(phrase), StringComparison.Ordinal));

    private static WarehouseAssistantPlannedQuery Plan(
        WarehouseAssistantIntent intent,
        WarehouseAssistantQueryKind kind,
        decimal confidence,
        string? warehouse = null,
        string? location = null,
        string? stockGroup = null,
        string? project = null,
        string? status = null,
        WarehouseAssistantStockMeasure? measure = null,
        WarehouseAssistantSortDirection sort = WarehouseAssistantSortDirection.None,
        int? limit = null,
        bool excludeZero = false,
        bool excludeCancelled = false,
        bool activeOnly = false,
        string? navigationTopic = null,
        IReadOnlyList<string>? reasons = null) =>
        new(intent, kind, confidence, warehouse, location, stockGroup, project, status, measure, sort, limit,
            excludeZero, excludeCancelled, activeOnly, navigationTopic, reasons);

    [GeneratedRegex(@"\b([a-z0-9][a-z0-9._/-]*)\s+(?:(?:numarali|nolu)\s+)?(?:depo|deposu|depoda|depodaki|deposunda|deposundaki|ambar|ambari|ambarin|ambarda|ambardaki|ambarinda|ambarindaki)\b", RegexOptions.CultureInvariant)]
    private static partial Regex WarehouseBeforeRegex();

    [GeneratedRegex(@"\b(\d+)\s+(?:numarali|nolu)\s+(?:depo|ambar)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedWarehouseRegex();

    [GeneratedRegex(@"\b([a-z0-9][a-z0-9._/-]*)\s+(?:lokasyonunda|lokasyonu|rafinda|rafi|raf|gozunde|gozu|goz|hucresinde|hucre)\b", RegexOptions.CultureInvariant)]
    private static partial Regex LocationBeforeRegex();

    [GeneratedRegex(@"(?<!stok\s)(?<!urun\s)(?<!malzeme\s)(?<!seri\s)\b[a-z][a-z0-9]*[/_-][a-z0-9][a-z0-9/_-]*\b", RegexOptions.CultureInvariant)]
    private static partial Regex LocationCodeRegex();

    [GeneratedRegex(@"\bprj[-/._][a-z0-9-]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectRegex();

    [GeneratedRegex(@"\b(.{2,40}?)\s+grubundaki\s+stok", RegexOptions.CultureInvariant)]
    private static partial Regex StockGroupRegex();

    [GeneratedRegex(@"\b([a-z0-9_-]{2,40})\s+(?:tarafindaki\s+)?(?:stoklari|stoklarini|urunleri|urunlerini|mallari|mallarini)\s+(?:kiyasla|karsilastir|yan\s+yana)", RegexOptions.CultureInvariant)]
    private static partial Regex ColloquialStockGroupRegex();

    [GeneratedRegex(@"\b([a-z0-9_-]{2,40})\s+grubunu\s+(?:kiyasla|karsilastir|mukayese)", RegexOptions.CultureInvariant)]
    private static partial Regex GroupComparisonRegex();

    [GeneratedRegex(@"(?:en\s+(?:fazla|az|yuksek)\s+)(\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex LimitRegex();

    [GeneratedRegex(@"\ben\s+(?:cok|dusuk|az|fazla|yuksek)(?:\s+[a-z]+){0,2}\s+(\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex FlexibleLimitRegex();

    [GeneratedRegex(@"\bilk\s+(\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex FirstLimitRegex();

    [GeneratedRegex(@"\b(\d+)\s+urun", RegexOptions.CultureInvariant)]
    private static partial Regex ItemLimitRegex();
}
