using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Modules.Procurement.Domain;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Application;

public sealed partial class GeneratorProductionService
{
    private readonly record struct MaterialKey(long StockId, long? YapCodeId, long WarehouseId, string UnitCode);
    private sealed record MaterialSupplyRow(MaterialKey Key, DateTime AvailableAtUtc, decimal Quantity, string Source, string? ProjectCode = null);
    private sealed record PlanningMaterialRequirement(
        long ProjectId, long ProductId, long RouteOperationId, long StockId, long? YapCodeId, long WarehouseId,
        string UnitCode, string StockCode, string? StockName, decimal QuantityPerUnit, decimal WasteRate,
        int NeedOffsetMinutes, bool IsMandatory, string Source);
    private sealed record MaterialAvailability(bool HasShortage, DateTime? AvailableAtUtc, string? Message);
    private sealed record MaterialPlanningAnalysis(
        MaterialPlanningContext Context,
        IReadOnlyList<GeneratorMaterialCoverageRow> Coverage,
        IReadOnlyList<GeneratorPlanningSuggestion> Suggestions);

    private sealed class TimePhasedMaterialLedger(IEnumerable<MaterialSupplyRow> supplies)
    {
        private readonly Dictionary<MaterialKey, List<MaterialSupplyRow>> _events = supplies
            .GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.OrderBy(e => e.AvailableAtUtc).ToList());

        public TimePhasedMaterialLedger Clone() => new(_events.Values.SelectMany(x => x));

        public decimal Available(MaterialKey key, DateTime atUtc, string projectCode) => EventsFor(key, projectCode)
            .Where(x => x.AvailableAtUtc <= atUtc).Sum(x => x.Quantity);

        public DateTime? FindAvailability(MaterialKey key, decimal quantity, DateTime requiredAtUtc, string projectCode)
        {
            var events = EventsFor(key, projectCode).ToArray();
            var dates = events.Where(x => x.AvailableAtUtc >= requiredAtUtc).Select(x => x.AvailableAtUtc)
                .Append(requiredAtUtc).Distinct().OrderBy(x => x);
            foreach (var date in dates)
                if (events.Where(x => x.AvailableAtUtc <= date).Sum(x => x.Quantity) >= quantity) return date;
            return null;
        }

        public void Reserve(MaterialKey key, decimal quantity, DateTime atUtc, string source, string projectCode)
        {
            var normalizedProjectCode = NormalizeProjectCode(projectCode);
            var localAvailable = PoolAvailable(key, atUtc, null);
            var localQuantity = Math.Min(quantity, localAvailable);
            if (localQuantity > 0) Add(new MaterialSupplyRow(key, atUtc, -localQuantity, source));
            var remaining = quantity - localQuantity;
            var globalKey = GlobalKey(key);
            var projectAvailable = PoolAvailable(globalKey, atUtc, normalizedProjectCode);
            var projectQuantity = Math.Min(remaining, projectAvailable);
            if (projectQuantity > 0) Add(new MaterialSupplyRow(globalKey, atUtc, -projectQuantity, source, normalizedProjectCode));
            remaining -= projectQuantity;
            var genericAvailable = PoolAvailable(globalKey, atUtc, null);
            var genericQuantity = Math.Min(remaining, genericAvailable);
            if (genericQuantity > 0) Add(new MaterialSupplyRow(globalKey, atUtc, -genericQuantity, source));
            remaining -= genericQuantity;
            if (remaining > 0) Add(new MaterialSupplyRow(globalKey, atUtc, -remaining, source));
        }

        private IEnumerable<MaterialSupplyRow> EventsFor(MaterialKey key, string projectCode)
        {
            var normalizedProjectCode = NormalizeProjectCode(projectCode);
            return _events.GetValueOrDefault(key, []).Concat(_events.GetValueOrDefault(GlobalKey(key), []))
                .Where(x => x.ProjectCode is null || x.ProjectCode == normalizedProjectCode);
        }

        private decimal PoolAvailable(MaterialKey key, DateTime atUtc, string? projectCode) => Math.Max(0,
            _events.GetValueOrDefault(key, []).Where(x => x.AvailableAtUtc <= atUtc && x.ProjectCode == projectCode).Sum(x => x.Quantity));

        private void Add(MaterialSupplyRow row)
        {
            if (!_events.TryGetValue(row.Key, out var events)) _events[row.Key] = events = [];
            events.Add(row);
            events.Sort((a, b) => a.AvailableAtUtc.CompareTo(b.AvailableAtUtc));
        }

        private static MaterialKey GlobalKey(MaterialKey key) => new(key.StockId, key.YapCodeId, 0, key.UnitCode);
    }

    private sealed class MaterialPlanningContext(
        IReadOnlyDictionary<(long ProjectId, long RouteOperationId), PlanningMaterialRequirement[]> definitions,
        IReadOnlyDictionary<long, string> projectCodes,
        TimePhasedMaterialLedger ledger)
    {
        public IReadOnlyDictionary<(long ProjectId, long RouteOperationId), PlanningMaterialRequirement[]> Definitions { get; } = definitions;

        public MaterialAvailability Find(long projectId, long routeOperationId, DateTime candidateStartUtc)
        {
            if (!Definitions.TryGetValue((projectId, routeOperationId), out var rows) || rows.Length == 0)
                return new(false, candidateStartUtc, null);
            var operationStart = candidateStartUtc;
            var missing = new List<string>();
            for (var pass = 0; pass < rows.Length + 2; pass++)
            {
                var changed = false;
                foreach (var row in rows.Where(x => x.IsMandatory))
                {
                    var quantity = RequiredQuantity(row);
                    var needAt = operationStart.AddMinutes(row.NeedOffsetMinutes);
                    var availableAt = ledger.FindAvailability(Key(row), quantity, needAt, ProjectCode(projectId));
                    if (!availableAt.HasValue)
                    {
                        missing.Add($"{row.StockCode} için {quantity:0.###} {row.UnitCode} arz bulunamadı");
                        continue;
                    }
                    var requiredStart = availableAt.Value.AddMinutes(-row.NeedOffsetMinutes);
                    if (requiredStart > operationStart) { operationStart = requiredStart; changed = true; }
                }
                if (!changed) break;
            }
            return missing.Count > 0
                ? new(true, null, string.Join("; ", missing.Distinct()))
                : new(false, operationStart, null);
        }

        public void Reserve(long projectId, long routeOperationId, DateTime operationStartUtc, string source)
        {
            if (!Definitions.TryGetValue((projectId, routeOperationId), out var rows)) return;
            foreach (var row in rows.Where(x => x.IsMandatory))
                ledger.Reserve(Key(row), RequiredQuantity(row), operationStartUtc.AddMinutes(row.NeedOffsetMinutes), source, ProjectCode(projectId));
        }

        public DateTime? EstimateProjectRelease(
            GeneratorProductionProject project,
            IEnumerable<GeneratorProductionRoute> routes,
            DateTime candidateStartUtc)
        {
            var result = candidateStartUtc;
            var operationIds = routes.SelectMany(x => x.Operations).Where(x => x.IsActive).Select(x => x.Id).ToHashSet();
            var materialGroups = Definitions
                .Where(x => x.Key.ProjectId == project.Id && operationIds.Contains(x.Key.RouteOperationId))
                .SelectMany(x => x.Value).Where(x => x.IsMandatory).GroupBy(Key);
            foreach (var materialGroup in materialGroups)
            {
                var quantity = materialGroup.Sum(RequiredQuantity);
                var needOffset = materialGroup.Min(x => x.NeedOffsetMinutes);
                var availableAt = ledger.FindAvailability(materialGroup.Key, quantity, candidateStartUtc.AddMinutes(needOffset), ProjectCode(project.Id));
                if (!availableAt.HasValue) return null;
                var requiredStart = availableAt.Value.AddMinutes(-needOffset);
                if (requiredStart > result) result = requiredStart;
            }
            return result;
        }

        private static MaterialKey Key(PlanningMaterialRequirement row) =>
            new(row.StockId, row.YapCodeId, row.WarehouseId, row.UnitCode.ToUpperInvariant());
        private static decimal RequiredQuantity(PlanningMaterialRequirement row) =>
            decimal.Round(row.QuantityPerUnit * (1 + row.WasteRate / 100m), 6, MidpointRounding.AwayFromZero);
        private string ProjectCode(long projectId) => projectCodes.GetValueOrDefault(projectId)
            ?? throw new InvalidOperationException($"{projectId} projesinin malzeme ayırma kodu bulunamadı.");
    }

    public async Task<GeneratorPlanningAssistantResult> GetPlanningAssistantAsync(CancellationToken ct = default)
    {
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        var projects = await Projects.Query().Include(x => x.Product)
            .Where(x => x.Status == GeneratorProjectStatus.Draft || x.Status == GeneratorProjectStatus.ReadyToPlan || x.Status == GeneratorProjectStatus.Planned)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.PlannedDeliveryAtUtc).Take(200).ToListAsync(ct);
        if (projects.Count == 0) return new([], [], DateTime.UtcNow);
        var routes = await uow.Repository<GeneratorProductionRoute>().Query().Where(x => x.IsActive)
            .Include(x => x.Operations.Where(o => o.IsActive)).ToListAsync(ct);
        var productIds = projects.Where(p => p.ProductId.HasValue).Select(p => p.ProductId!.Value).Distinct().ToArray();
        var productRouteRows = await uow.Repository<GeneratorProductionProductRoute>().Query()
            .Where(x => x.IsActive && productIds.Contains(x.ProductId))
            .ToListAsync(ct);
        var routeMap = new Dictionary<long, IReadOnlyList<GeneratorProductionRoute>>();
        var suggestions = new List<GeneratorPlanningSuggestion>();
        foreach (var project in projects)
        {
            var selectedParts = GeneratorProductionPlanningPolicy.SelectRoutes(project).ToHashSet();
            var selected = project.ProductId.HasValue
                ? productRouteRows.Where(x => x.ProductId == project.ProductId && selectedParts.Contains(x.PartType))
                    .Select(x => routes.FirstOrDefault(r => r.Id == x.RouteId)).Where(x => x is not null).Cast<GeneratorProductionRoute>().ToArray()
                : routes.Where(x => selectedParts.Contains(x.PartType)).GroupBy(x => x.PartType).Where(x => x.Count() == 1).Select(x => x.Single()).ToArray();
            routeMap[project.Id] = selected;
            if (project.ProductId.HasValue && selected.Select(x => x.PartType).Distinct().Count() != selectedParts.Count)
                suggestions.Add(new("MASTER_DATA_REQUIRED", GeneratorRuleSeverity.Error, project.Id, project.ProjectCode, null, null,
                    "Ürün rotası tamamlanmalı", "Seçilen ürünün proje kapsamındaki her bileşeni için aktif rota eşleştirmesi yok.",
                    "Ürün, rota ve istasyon yeteneği tanımlarını tamamlayın."));
            else if (!project.ProductId.HasValue)
                suggestions.Add(new("PRODUCT_ASSIGNMENT_REQUIRED", GeneratorRuleSeverity.Warning, project.Id, project.ProjectCode, null, null,
                    "Projeye ürün tanımı bağlayın", "Eski tip metni kapasiteyi planlar ancak stok ve satınalma uygunluğunu hesaplayamaz.",
                    "Projeyi bir jeneratör ürün ana verisiyle eşleştirin."));
        }
        var analysis = await CreateMaterialPlanningAnalysisAsync(projects, routeMap, policy, DateTime.UtcNow, [], ct);
        return new GeneratorPlanningAssistantResult(analysis.Coverage, suggestions.Concat(analysis.Suggestions).ToArray(), DateTime.UtcNow);
    }

    private async Task<MaterialPlanningAnalysis> CreateMaterialPlanningAnalysisAsync(
        IReadOnlyList<GeneratorProductionProject> projects,
        IReadOnlyDictionary<long, IReadOnlyList<GeneratorProductionRoute>> routesByProject,
        GeneratorProductionPolicy policy,
        DateTime scheduleStart,
        IReadOnlyCollection<long> excludedProjectIds,
        CancellationToken ct)
    {
        var excludedIds = excludedProjectIds.ToArray();
        var existingOperations = await uow.Repository<GeneratorProductionOperation>().Query()
            .Where(x => !excludedIds.Contains(x.ProjectId)
                && x.Status != GeneratorOperationStatus.Cancelled && x.Status != GeneratorOperationStatus.Completed)
            .Select(x => new { x.RouteOperationId, RouteId = x.RouteOperation.RouteId, x.PlannedStartAtUtc, x.ProjectId, x.Id })
            .ToListAsync(ct);
        var requestedProjectIds = projects.Select(x => x.Id).ToHashSet();
        var reservationProjectIds = existingOperations.Select(x => x.ProjectId).Distinct()
            .Where(x => !requestedProjectIds.Contains(x)).ToArray();
        var reservationProjects = reservationProjectIds.Length == 0
            ? []
            : await Projects.Query().Include(x => x.Product).Where(x => reservationProjectIds.Contains(x.Id)).ToListAsync(ct);
        var reservationRouteIds = existingOperations.Where(x => reservationProjectIds.Contains(x.ProjectId))
            .Select(x => x.RouteId).Distinct().ToArray();
        var reservationRoutes = reservationRouteIds.Length == 0
            ? []
            : await uow.Repository<GeneratorProductionRoute>().Query()
                .Include(x => x.Operations.Where(operation => operation.IsActive))
                .Where(x => reservationRouteIds.Contains(x.Id)).ToListAsync(ct);
        var planningProjects = projects.Concat(reservationProjects).DistinctBy(x => x.Id).ToArray();
        var planningRoutesByProject = routesByProject.ToDictionary(x => x.Key, x => x.Value);
        foreach (var project in reservationProjects)
        {
            var routeIds = existingOperations.Where(x => x.ProjectId == project.Id).Select(x => x.RouteId).ToHashSet();
            planningRoutesByProject[project.Id] = reservationRoutes.Where(x => routeIds.Contains(x.Id)).ToArray();
        }

        var productIds = planningProjects.Where(x => x.ProductId.HasValue).Select(x => x.ProductId!.Value).Distinct().ToArray();
        var routeOperationIds = planningRoutesByProject.Values.SelectMany(x => x).SelectMany(x => x.Operations).Select(x => x.Id).Distinct().ToArray();
        var overrides = await uow.Repository<GeneratorProductionOperationMaterial>().Query()
            .Where(x => productIds.Contains(x.ProductId) && routeOperationIds.Contains(x.RouteOperationId))
            .ToListAsync(ct);
        var requirements = new List<PlanningMaterialRequirement>();
        foreach (var project in planningProjects.Where(x => x.ProductId.HasValue))
            requirements.AddRange(overrides.Where(x => x.ProductId == project.ProductId).Select(x => new PlanningMaterialRequirement(
                project.Id, x.ProductId, x.RouteOperationId, x.StockId, x.YapCodeId, x.WarehouseId,
                x.UnitCode, x.StockCodeSnapshot, x.StockNameSnapshot, x.QuantityPerUnit, x.WasteRate,
                x.NeedOffsetMinutes, x.IsMandatory, "GeneratorOverride")));

        await AddProductionMaterialRequirementsAsync(requirements, planningProjects, planningRoutesByProject, ct);
        await AddSourceRecipeRequirementsAsync(requirements, planningProjects, planningRoutesByProject, ct);

        // Ürün/operasyon seviyesinde tanımlanan jeneratör kaydı bir istisnadır. Aynı operasyon için
        // Production reçetesinden gelen satırların yerini alır; diğer operasyonlar Production kaynağını kullanmaya devam eder.
        var overrideKeys = requirements.Where(x => x.Source == "GeneratorOverride")
            .Select(x => (x.ProjectId, x.RouteOperationId)).ToHashSet();
        requirements.RemoveAll(x => x.Source != "GeneratorOverride" && overrideKeys.Contains((x.ProjectId, x.RouteOperationId)));

        var suggestions = new List<GeneratorPlanningSuggestion>();
        foreach (var project in projects)
        {
            var operationIds = routesByProject.GetValueOrDefault(project.Id, []).SelectMany(x => x.Operations).Select(x => x.Id).ToHashSet();
            if (operationIds.Count > 0 && !requirements.Any(x => x.ProjectId == project.Id && operationIds.Contains(x.RouteOperationId) && x.IsMandatory))
                suggestions.Add(new("MATERIAL_DEFINITION_REQUIRED", GeneratorRuleSeverity.Error, project.Id, project.ProjectCode, null, null,
                    "Malzeme ana verisi tamamlanmalı",
                    $"{project.ProjectCode} için seçilen rotalarda zorunlu BOM, Production malzeme ihtiyacı veya operasyon malzeme istisnası bulunamadı.",
                    "Ürünün reçete kaynağını ve operasyon-malzeme eşlemelerini tamamlayıp planı yeniden hesaplayın."));
        }
        foreach (var project in reservationProjects)
        {
            var operationIds = existingOperations.Where(x => x.ProjectId == project.Id).Select(x => x.RouteOperationId).ToHashSet();
            if (operationIds.Count > 0 && !requirements.Any(x => x.ProjectId == project.Id && operationIds.Contains(x.RouteOperationId) && x.IsMandatory))
                suggestions.Add(new("MATERIAL_COMMITMENT_UNKNOWN", GeneratorRuleSeverity.Error, null, project.ProjectCode, null, null,
                    "Mevcut planın malzeme taahhüdü hesaplanamadı",
                    $"{project.ProjectCode} projesinin açık operasyonları var ancak zorunlu malzeme kaynağı çözümlenemedi; yeni plan kullanılabilir stoku fazla gösterebilir.",
                    "Önce mevcut projenin ürün, reçete ve operasyon malzeme eşlemelerini tamamlayın."));
        }

        var keys = requirements.Select(KeyOf).Distinct().ToArray();
        var stockIds = keys.Select(x => x.StockId).Distinct().ToArray();
        var warehouseIds = keys.Select(x => x.WarehouseId).Distinct().ToArray();
        var balances = keys.Length == 0 ? [] : await uow.Repository<WarehouseStockBalance>().Query()
            .Where(x => stockIds.Contains(x.StockId) && warehouseIds.Contains(x.WarehouseId) && x.StockStatus == "Available")
            .Select(x => new { x.StockId, x.YapCodeId, x.WarehouseId, x.UnitCode, x.AvailableQuantity }).ToListAsync(ct);
        var purchaseLines = keys.Length == 0 ? [] : await uow.Repository<ProcurementPurchaseOrderLine>().Query()
            .Where(x => x.StockId.HasValue && stockIds.Contains(x.StockId.Value)
                && (x.Order.Status == ProcurementOrderStatus.Approved || x.Order.Status == ProcurementOrderStatus.SentToSupplier || x.Order.Status == ProcurementOrderStatus.PartiallyReceived)
                && x.OrderedQuantity - x.ReceivedQuantity - x.CancelledQuantity > 0)
            .Select(x => new { StockId = x.StockId!.Value, x.UnitCode, Quantity = x.OrderedQuantity - x.ReceivedQuantity - x.CancelledQuantity,
                DeliveryDate = x.DeliveryDate ?? x.Order.DeliveryDate, x.Order.OrderNo, ProjectCode = x.ProjectCode ?? x.Order.ProjectCode }).ToListAsync(ct);

        var supplies = new List<MaterialSupplyRow>();
        foreach (var key in keys)
        {
            var onHand = balances.Where(x => x.StockId == key.StockId && x.WarehouseId == key.WarehouseId && x.YapCodeId == key.YapCodeId
                    && x.UnitCode == key.UnitCode).Sum(x => x.AvailableQuantity);
            if (onHand != 0) supplies.Add(new(key, DateTime.MinValue, onHand, "WarehouseStockBalance"));
        }
        foreach (var line in purchaseLines.Where(x => x.DeliveryDate.HasValue))
        {
            var available = PurchaseAvailableAt(line.DeliveryDate!.Value, policy.InboundQualityBufferDays);
            if (available <= scheduleStart)
            {
                var affected = projects.Where(x => ProjectSupplyMatches(line.ProjectCode, x.ProjectCode)
                    && requirements.Any(requirement => requirement.ProjectId == x.Id && requirement.StockId == line.StockId && requirement.IsMandatory)).ToArray();
                foreach (var project in affected)
                    suggestions.Add(new("PURCHASE_ORDER_OVERDUE", GeneratorRuleSeverity.Warning, project.Id, project.ProjectCode, null, line.StockId,
                        $"{line.OrderNo} satınalma termini geçmiş",
                        $"{line.OrderNo} siparişindeki açık miktar teslim ve kalite tampon tarihini geçtiği halde henüz teslim alınmadı; kullanılabilir stok sayılmadı.",
                        "Tedarikçiden yeni teyitli termin alın veya malzeme açığı için alternatif tedarik değerlendirin."));
                continue;
            }
            supplies.Add(new(new MaterialKey(line.StockId, null, 0, line.UnitCode.ToUpperInvariant()), available, line.Quantity,
                $"Procurement:{line.OrderNo}", NormalizeProjectCode(line.ProjectCode)));
        }
        var ledger = new TimePhasedMaterialLedger(supplies);
        var definitionsByOperation = requirements.GroupBy(x => (x.ProjectId, x.RouteOperationId)).ToDictionary(x => x.Key, x => x.ToArray());
        var projectCodes = planningProjects.ToDictionary(x => x.Id, x => x.ProjectCode);

        foreach (var operation in existingOperations)
            if (definitionsByOperation.TryGetValue((operation.ProjectId, operation.RouteOperationId), out var operationMaterials))
                foreach (var material in operationMaterials.Where(x => x.IsMandatory))
                    ledger.Reserve(KeyOf(material), RequiredOf(material), operation.PlannedStartAtUtc.AddMinutes(material.NeedOffsetMinutes),
                        $"Planned:{operation.ProjectId}:{operation.Id}", projectCodes[operation.ProjectId]);

        var coverage = new List<GeneratorMaterialCoverageRow>();
        var coverageLedger = ledger.Clone();
        foreach (var project in projects)
        {
            var operationIds = routesByProject.GetValueOrDefault(project.Id, []).SelectMany(x => x.Operations).Select(x => x.Id).ToHashSet();
            var projectDefinitions = requirements.Where(x => x.ProjectId == project.Id && operationIds.Contains(x.RouteOperationId) && x.IsMandatory).ToArray();
            foreach (var materialGroup in projectDefinitions.GroupBy(KeyOf))
            {
                var definition = materialGroup.First(); var key = materialGroup.Key;
                var perUnit = materialGroup.Sum(RequiredOf); var required = perUnit * project.Quantity;
                var availableNow = Math.Max(0, coverageLedger.Available(key, scheduleStart, project.ProjectCode));
                var openPurchase = purchaseLines.Where(x => x.StockId == key.StockId
                    && string.Equals(x.UnitCode, key.UnitCode, StringComparison.OrdinalIgnoreCase)
                    && ProjectSupplyMatches(x.ProjectCode, project.ProjectCode) && x.DeliveryDate.HasValue
                    && PurchaseAvailableAt(x.DeliveryDate.Value, policy.InboundQualityBufferDays) > scheduleStart).Sum(x => x.Quantity);
                var globalKey = new MaterialKey(key.StockId, key.YapCodeId, 0, key.UnitCode);
                var nextSupply = supplies.Where(x => (x.Key == key || x.Key == globalKey) && x.Quantity > 0 && x.AvailableAtUtc > scheduleStart
                    && ProjectSupplyMatches(x.ProjectCode, project.ProjectCode)).Select(x => (DateTime?)x.AvailableAtUtc).Min();
                var maxNow = perUnit <= 0 ? project.Quantity : Math.Max(0, (int)decimal.Floor(availableNow / perUnit));
                var warehouse = await uow.Repository<WarehouseEntity>().FindByIdAsync(key.WarehouseId, false, ct);
                coverage.Add(new(project.Id, project.ProjectCode, key.StockId, definition.StockCode, definition.StockName ?? definition.StockCode,
                    key.WarehouseId, warehouse?.WarehouseCode ?? 0, key.UnitCode, required, availableNow, openPurchase, nextSupply,
                    Math.Max(0, required - availableNow - openPurchase), maxNow));
                coverageLedger.Reserve(key, required, scheduleStart, $"Coverage:{project.Id}", project.ProjectCode);
            }
        }

        foreach (var project in projects)
        {
            var shortages = coverage.Where(x => x.ProjectId == project.Id && x.MaximumProducibleNow < project.Quantity).ToArray();
            foreach (var row in shortages)
            {
                var alternative = projects.FirstOrDefault(candidate => candidate.Id != project.Id
                    && coverage.Any(x => x.ProjectId == candidate.Id)
                    && coverage.Where(x => x.ProjectId == candidate.Id).All(x => x.MaximumProducibleNow > 0));
                if (row.NextSupplyAtUtc.HasValue)
                    suggestions.Add(new("MATERIAL_WAIT", GeneratorRuleSeverity.Warning, project.Id, project.ProjectCode, null, row.StockId,
                        $"{project.ProjectCode} için bugün en fazla {Math.Min(project.Quantity, row.MaximumProducibleNow)} adet",
                        $"{row.StockCode} malzemesi nedeniyle kalan üretim {row.NextSupplyAtUtc:dd.MM.yyyy HH:mm} sonrasına taşınmalı. Teyitli satınalma arzı kalite tamponuyla birlikte hesaba katıldı.",
                        alternative is null ? "Kalan üniteleri malzeme kullanılabilir tarihine taşıyın." : $"Bekleme aralığında {alternative.ProjectCode} projesini öne alın.",
                        row.NextSupplyAtUtc, alternative?.Id, alternative?.ProjectCode));
                else
                    suggestions.Add(new("PURCHASE_REQUEST_PROPOSAL", GeneratorRuleSeverity.Error, project.Id, project.ProjectCode, null, row.StockId,
                        $"{row.StockCode} için satınalma talebi önerisi",
                        $"{row.ShortageQuantity:0.###} {row.UnitCode} net açık var ve teyitli açık satınalma siparişi bu açığı kapatmıyor.",
                        "Otomatik sipariş vermeden önce satınalma talebi taslağı oluşturup onaya gönderin."));
            }
        }
        return new(new MaterialPlanningContext(definitionsByOperation, projectCodes, ledger), coverage, suggestions);
    }

    private async Task AddProductionMaterialRequirementsAsync(
        List<PlanningMaterialRequirement> target,
        IReadOnlyList<GeneratorProductionProject> projects,
        IReadOnlyDictionary<long, IReadOnlyList<GeneratorProductionRoute>> routesByProject,
        CancellationToken ct)
    {
        var headerIds = projects.Where(x => x.ProductionHeaderId.HasValue)
            .Select(x => x.ProductionHeaderId!.Value).Distinct().ToArray();
        if (headerIds.Length == 0) return;

        var rows = await uow.Repository<ProductionMaterialRequirement>().Query()
            .Where(x => headerIds.Contains(x.Order.ProductionHeaderId) && x.RequiredQuantity > x.ConsumedQuantity)
            .Select(x => new
            {
                x.Order.ProductionHeaderId,
                x.Order.SequenceNo,
                x.Order.RoutingReference,
                x.StockId,
                x.YapCodeId,
                x.SourceWarehouseId,
                x.UnitCode,
                x.StockCodeSnapshot,
                x.StockNameSnapshot,
                RemainingQuantity = x.RequiredQuantity - x.ConsumedQuantity,
                x.IsMandatory
            }).ToListAsync(ct);

        foreach (var project in projects.Where(x => x.ProductionHeaderId.HasValue))
        {
            var operationRows = routesByProject.GetValueOrDefault(project.Id, []).SelectMany(x => x.Operations).ToArray();
            foreach (var row in rows.Where(x => x.ProductionHeaderId == project.ProductionHeaderId))
            {
                var operation = ResolveRouteOperation(operationRows, row.SequenceNo, row.RoutingReference);
                if (operation is null) continue;
                target.Add(new PlanningMaterialRequirement(
                    project.Id, project.ProductId ?? 0, operation.Id, row.StockId, row.YapCodeId, row.SourceWarehouseId,
                    row.UnitCode, row.StockCodeSnapshot, row.StockNameSnapshot,
                    row.RemainingQuantity / Math.Max(1, project.Quantity), 0, 0, row.IsMandatory, "ProductionMaterialRequirement"));
            }
        }
    }

    private async Task AddSourceRecipeRequirementsAsync(
        List<PlanningMaterialRequirement> target,
        IReadOnlyList<GeneratorProductionProject> projects,
        IReadOnlyDictionary<long, IReadOnlyList<GeneratorProductionRoute>> routesByProject,
        CancellationToken ct)
    {
        var sourceProjects = projects.Where(x => !string.IsNullOrWhiteSpace(x.ExternalWorkOrderNo)
                && target.All(r => r.ProjectId != x.Id || r.Source != "ProductionMaterialRequirement"))
            .ToArray();
        if (sourceProjects.Length == 0) return;

        var workOrderNumbers = sourceProjects.Select(x => x.ExternalWorkOrderNo!).Distinct().ToArray();
        var sourceOrders = await uow.Repository<ProductionSourceWorkOrder>().Query()
            .Include(x => x.RecipeLines)
            .Where(x => workOrderNumbers.Contains(x.WorkOrderNumber)
                && (x.Status == ProductionSourceOrderStatus.Ready || x.Status == ProductionSourceOrderStatus.Released))
            .OrderByDescending(x => x.RevisionNumber).ThenByDescending(x => x.SourceUpdatedAtUtc).ToListAsync(ct);
        if (sourceOrders.Count == 0) return;

        var selectedOrders = sourceProjects.Select(project => new
            {
                Project = project,
                Source = sourceOrders.FirstOrDefault(x => x.WorkOrderNumber == project.ExternalWorkOrderNo
                    && (string.IsNullOrWhiteSpace(project.SourceSystemCode) || x.SourceSystemCode == project.SourceSystemCode))
            })
            .Where(x => x.Source is not null).ToArray();
        if (selectedOrders.Length == 0) return;

        var stockCodes = selectedOrders.SelectMany(x => x.Source!.RecipeLines).Select(x => x.ComponentStockCode).Distinct().ToArray();
        var stocks = await uow.Repository<StockEntity>().Query().Where(x => stockCodes.Contains(x.ErpStockCode)).ToListAsync(ct);
        var warehouseCodes = selectedOrders.Select(x => x.Source!.SourceWarehouseCode).Distinct().ToArray();
        var warehouses = await uow.Repository<WarehouseEntity>().Query().Where(x => warehouseCodes.Contains(x.WarehouseCode)).ToListAsync(ct);
        var configurationCodes = selectedOrders.SelectMany(x => x.Source!.RecipeLines)
            .Where(x => x.ComponentConfigurationCode != null).Select(x => x.ComponentConfigurationCode!).Distinct().ToArray();
        var yapCodes = configurationCodes.Length == 0 ? [] : await uow.Repository<YapCodeEntity>().Query()
            .Where(x => configurationCodes.Contains(x.ConfigurationCode)).ToListAsync(ct);

        foreach (var selection in selectedOrders)
        {
            var project = selection.Project;
            var source = selection.Source!;
            var warehouse = warehouses.FirstOrDefault(x => x.WarehouseCode == source.SourceWarehouseCode);
            if (warehouse is null) continue;
            var operationRows = routesByProject.GetValueOrDefault(project.Id, []).SelectMany(x => x.Operations).ToArray();
            foreach (var line in source.RecipeLines.OrderBy(x => x.LineNumber))
            {
                var operation = ResolveRouteOperation(operationRows, line.OperationNumber, null);
                var stock = stocks.FirstOrDefault(x => x.ErpStockCode == line.ComponentStockCode);
                if (operation is null || stock is null) continue;
                var yapCode = string.IsNullOrWhiteSpace(line.ComponentConfigurationCode) ? null : yapCodes
                    .Where(x => x.ConfigurationCode == line.ComponentConfigurationCode)
                    .OrderByDescending(x => x.StockId == stock.Id).ThenBy(x => x.Id).FirstOrDefault();
                var fixedWastePerUnit = line.FixedWasteQuantity / Math.Max(1m, source.PlannedQuantity);
                target.Add(new PlanningMaterialRequirement(
                    project.Id, project.ProductId ?? 0, operation.Id, stock.Id, yapCode?.Id, warehouse.Id,
                    stock.BaseUnitCode, stock.ErpStockCode, stock.StockName,
                    line.RecipeQuantity + line.VariableWasteQuantity + fixedWastePerUnit,
                    0, 0, line.IsMandatory, $"ProductionSourceRecipe:{source.WorkOrderNumber}:R{source.RevisionNumber}"));
            }
        }
    }

    private static GeneratorProductionRouteOperation? ResolveRouteOperation(
        IReadOnlyCollection<GeneratorProductionRouteOperation> operations,
        int operationNumber,
        string? routingReference)
    {
        if (!string.IsNullOrWhiteSpace(routingReference))
        {
            var byCode = operations.FirstOrDefault(x => string.Equals(x.OperationCode, routingReference.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byCode is not null) return byCode;
        }
        return operations.Where(x => x.Sequence == operationNumber).OrderBy(x => x.RouteId).ThenBy(x => x.Id).FirstOrDefault();
    }

    private async Task<MaterialAvailability> CheckOperationMaterialAvailabilityAsync(
        GeneratorProductionOperation operation, DateTime plannedStart, int qualityBufferDays, CancellationToken ct)
    {
        if (!operation.Project.ProductId.HasValue) return new(false, plannedStart, null);
        var policy = await GetRequiredPolicyEntityAsync(false, ct);
        policy.InboundQualityBufferDays = qualityBufferDays;
        var route = await uow.Repository<GeneratorProductionRoute>().Query().Include(x => x.Operations)
            .FirstAsync(x => x.Id == operation.RouteOperation.RouteId, ct);
        var routes = new Dictionary<long, IReadOnlyList<GeneratorProductionRoute>> { [operation.ProjectId] = [route] };
        var analysis = await CreateMaterialPlanningAnalysisAsync([operation.Project], routes, policy, plannedStart, [operation.ProjectId], ct);
        var blocking = analysis.Suggestions.FirstOrDefault(x => x.Severity == GeneratorRuleSeverity.Error
            && (!x.ProjectId.HasValue || x.ProjectId == operation.ProjectId));
        if (blocking is not null) return new(true, null, $"{blocking.Title}: {blocking.Explanation}");
        return analysis.Context.Find(operation.ProjectId, operation.RouteOperationId, plannedStart);
    }

    private static MaterialKey KeyOf(PlanningMaterialRequirement row) =>
        new(row.StockId, row.YapCodeId, row.WarehouseId, row.UnitCode.ToUpperInvariant());
    private static decimal RequiredOf(PlanningMaterialRequirement row) =>
        decimal.Round(row.QuantityPerUnit * (1 + row.WasteRate / 100m), 6, MidpointRounding.AwayFromZero);
    private static string? NormalizeProjectCode(string? value) => GeneratorProductionPlanningPolicy.NormalizeProjectCode(value);
    private static DateTime PurchaseAvailableAt(DateOnly deliveryDate, int qualityBufferDays) => DateTime.SpecifyKind(
        deliveryDate.AddDays(qualityBufferDays).ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Utc);
    private static bool ProjectSupplyMatches(string? supplyProjectCode, string projectCode) =>
        GeneratorProductionPlanningPolicy.CanUseProjectSupply(supplyProjectCode, projectCode);
}
