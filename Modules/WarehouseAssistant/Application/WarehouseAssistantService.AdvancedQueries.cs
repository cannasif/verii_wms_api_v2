using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.InventoryCount.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed partial class WarehouseAssistantService
{
    private async Task<ExecutionResult> ExecuteWarehouseOverviewAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewLocations && !access.CanViewStockBalances)
            return Denied(resolution.Intent, AdvancedMessage("AdvancedWarehouseDenied", "Depo özetini görüntüleme yetkiniz bulunmuyor."));

        var warehouses = await ResolveAuthorizedWarehousesAsync(actorUserId, branchCode, resolution.WarehouseQuery, ct);
        if (resolution.ActiveOnly)
            return AdvancedResult(
                resolution,
                "warehouse-domain-limitation",
                AdvancedMessage("AdvancedWarehouseActiveUnsupported", "Depo kartında aktif/pasif alanı bulunmadığı için aktif depo filtresi güvenilir biçimde uygulanamıyor."),
                [],
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind));

        if (warehouses.Count == 0)
            return AdvancedResult(
                resolution,
                "warehouse-summary-query",
                AdvancedMessage("AdvancedWarehouseNotFound", "Yetkili depo kapsamınızda eşleşen depo bulunamadı."),
                [],
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind));

        var warehouseIds = warehouses.Select(x => x.Id).ToArray();
        if (resolution.QueryKind == WarehouseAssistantQueryKind.WarehouseStockTotals)
        {
            if (!access.CanViewStockBalances)
                return Denied(resolution.Intent, AdvancedMessage("AdvancedStockBalanceDenied", "Stok bakiyelerini görüntüleme yetkiniz bulunmuyor."));

            var totals = await unitOfWork.Repository<WarehouseStockBalance>().Query()
                .Where(x => x.BranchCode == branchCode && warehouseIds.Contains(x.WarehouseId))
                .GroupBy(x => new { x.WarehouseId, x.UnitCode })
                .Select(x => new
                {
                    x.Key.WarehouseId,
                    x.Key.UnitCode,
                    Physical = x.Sum(y => y.Quantity),
                    Reserved = x.Sum(y => y.ReservedQuantity),
                    Available = x.Sum(y => y.AvailableQuantity)
                })
                .ToListAsync(ct);
            var byId = warehouses.ToDictionary(x => x.Id);
            var rows = totals.Select(x =>
            {
                var warehouse = byId[x.WarehouseId];
                return new WarehouseAssistantAnalysisRow(
                    "WarehouseStockTotal", "Warehouse", warehouse.Id, warehouse.WarehouseCode.ToString(), warehouse.WarehouseName,
                    warehouse.WarehouseCode, warehouse.WarehouseName, UnitCode: x.UnitCode,
                    PhysicalQuantity: x.Physical, AvailableQuantity: x.Available, ReservedQuantity: x.Reserved,
                    Detail: "Miktarlar birim bazında ayrı tutulur.", Route: "/warehouse/stock-balances");
            }).Take(MaximumResultCount).ToArray();
            return AdvancedResult(
                resolution,
                "warehouse-stock-total-query",
                rows.Length == 0
                    ? AdvancedMessage("AdvancedWarehouseStockNone", "Yetkili depo kapsamında stok bakiyesi bulunamadı.")
                    : AdvancedMessage("AdvancedWarehouseStockFound", $"{rows.Length} depo/birim toplamı bulundu; farklı birimler birleştirilmedi."),
                rows,
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind, StockMeasure: resolution.StockMeasure));
        }

        if (resolution.QueryKind == WarehouseAssistantQueryKind.WarehouseLocations)
        {
            if (!access.CanViewLocations)
                return Denied(resolution.Intent, AdvancedMessage("AdvancedLocationDenied", "Lokasyonları görüntüleme yetkiniz bulunmuyor."));
            var locations = await unitOfWork.Repository<WarehouseLocation>().Query()
                .Where(x => x.BranchCode == branchCode && warehouseIds.Contains(x.WarehouseId) && x.IsActive)
                .OrderBy(x => x.WarehouseId).ThenBy(x => x.Code)
                .Take(MaximumResultCount)
                .ToListAsync(ct);
            var byId = warehouses.ToDictionary(x => x.Id);
            var rows = locations.Select(x =>
            {
                var warehouse = byId[x.WarehouseId];
                return LocationAnalysisRow(x, warehouse, "WarehouseLocation");
            }).ToArray();
            return AdvancedResult(
                resolution,
                "warehouse-location-query",
                AdvancedMessage("AdvancedWarehouseLocationsFound", $"Yetkili kapsamda {rows.Length} lokasyon listelendi."),
                rows,
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind));
        }

        var locationCounts = access.CanViewLocations
            ? await unitOfWork.Repository<WarehouseLocation>().Query()
                .Where(x => x.BranchCode == branchCode && warehouseIds.Contains(x.WarehouseId) && x.IsActive)
                .GroupBy(x => x.WarehouseId)
                .Select(x => new { WarehouseId = x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.WarehouseId, x => x.Count, ct)
            : [];
        var warehouseRows = warehouses.Take(MaximumResultCount)
            .Select(x => new WarehouseAssistantAnalysisRow(
                "Warehouse", "Warehouse", x.Id, x.WarehouseCode.ToString(), x.WarehouseName,
                x.WarehouseCode, x.WarehouseName,
                Detail: access.CanViewLocations && locationCounts.TryGetValue(x.Id, out var count) ? $"{count} aktif lokasyon" : null,
                Route: "/warehouse/locations"))
            .ToArray();
        var metrics = resolution.QueryKind == WarehouseAssistantQueryKind.WarehouseCount
            ? new[] { new WarehouseAssistantSummaryMetricRow("warehouseCount", "Yetkili depo", warehouses.Count, "depo", "Info", "Warehouse", "/warehouse/locations") }
            : [];
        return AdvancedResult(
            resolution,
            "warehouse-list-query",
            AdvancedMessage("AdvancedWarehouseFound", $"Yetkili kapsamınızda {warehouses.Count} depo bulundu."),
            warehouseRows,
            new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind),
            metrics);
    }

    private async Task<ExecutionResult> ExecuteLocationInventoryAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewLocations)
            return Denied(resolution.Intent, AdvancedMessage("AdvancedLocationDenied", "Lokasyonları görüntüleme yetkiniz bulunmuyor."));

        var warehouses = await ResolveAuthorizedWarehousesAsync(actorUserId, branchCode, resolution.WarehouseQuery, ct);
        var warehouseIds = warehouses.Select(x => x.Id).ToArray();
        var query = unitOfWork.Repository<WarehouseLocation>().Query()
            .Where(x => x.BranchCode == branchCode && warehouseIds.Contains(x.WarehouseId) && x.IsActive);
        if (resolution.QueryKind == WarehouseAssistantQueryKind.LocationListByType)
            query = query.Where(x => x.IsQuarantine || x.LocationType == LocationTypes.Quarantine);
        if (!string.IsNullOrWhiteSpace(resolution.LocationQuery))
        {
            var requested = WarehouseAssistantTextNormalizer.Normalize(resolution.LocationQuery);
            var candidateIds = (await query.Select(x => new { x.Id, x.Code, x.Name }).Take(5000).ToListAsync(ct))
                .Where(x => WarehouseAssistantTextNormalizer.Normalize(x.Code).Equals(requested, StringComparison.OrdinalIgnoreCase)
                    || WarehouseAssistantTextNormalizer.Normalize(x.Name).Equals(requested, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .ToArray();
            query = query.Where(x => candidateIds.Contains(x.Id));
        }

        var locations = await query.OrderBy(x => x.WarehouseId).ThenBy(x => x.Code).Take(MaximumResultCount).ToListAsync(ct);
        if (locations.Count == 0)
            return AdvancedResult(
                resolution,
                "location-query",
                AdvancedMessage("AdvancedLocationNotFound", "Yetkili depo kapsamınızda eşleşen lokasyon bulunamadı."),
                [],
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, LocationQuery: resolution.LocationQuery, QueryKind: resolution.QueryKind));

        var warehouseById = warehouses.ToDictionary(x => x.Id);
        if (resolution.QueryKind == WarehouseAssistantQueryKind.LocationListByType)
        {
            var listRows = locations.Select(x => LocationAnalysisRow(x, warehouseById[x.WarehouseId], "LocationByType")).ToArray();
            return AdvancedResult(resolution, "location-type-query",
                AdvancedMessage("AdvancedLocationsFound", $"{listRows.Length} lokasyon bulundu."), listRows,
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, LocationQuery: resolution.LocationQuery, QueryKind: resolution.QueryKind));
        }

        if (resolution.QueryKind != WarehouseAssistantQueryKind.LocationListByType && !access.CanViewStockBalances)
            return Denied(resolution.Intent, AdvancedMessage("AdvancedStockBalanceDenied", "Lokasyon içeriği için stok bakiyesi görüntüleme yetkisi gerekiyor."));

        var locationIds = locations.Select(x => x.Id).ToArray();
        var balances = await unitOfWork.Repository<LocationStockBalance>().Query()
            .Where(x => x.BranchCode == branchCode && locationIds.Contains(x.LocationId) && x.Quantity != 0)
            .GroupBy(x => new { x.LocationId, x.StockId, x.UnitCode })
            .Select(x => new
            {
                x.Key.LocationId,
                x.Key.StockId,
                x.Key.UnitCode,
                Physical = x.Sum(y => y.Quantity),
                Reserved = x.Sum(y => y.ReservedQuantity),
                Available = x.Sum(y => y.AvailableQuantity)
            })
            .Take(5000)
            .ToListAsync(ct);
        var stocks = await unitOfWork.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branchCode && balances.Select(y => y.StockId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var locationById = locations.ToDictionary(x => x.Id);

        if (resolution.QueryKind == WarehouseAssistantQueryKind.LocationEmptyCheck)
        {
            var occupiedIds = balances.Select(x => x.LocationId).ToHashSet();
            var emptyRows = locations.Select(x =>
            {
                var warehouse = warehouseById[x.WarehouseId];
                var isEmpty = !occupiedIds.Contains(x.Id);
                return LocationAnalysisRow(x, warehouse, "LocationOccupancy") with
                {
                    Status = isEmpty ? "Empty" : "Occupied",
                    Detail = isEmpty ? "Fiziksel stok bulunmuyor." : "Fiziksel stok bulunuyor."
                };
            }).ToArray();
            return AdvancedResult(resolution, "location-empty-query",
                AdvancedMessage("AdvancedLocationOccupancyFound", $"{emptyRows.Length} lokasyonun doluluk durumu kontrol edildi."), emptyRows,
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, LocationQuery: resolution.LocationQuery, QueryKind: resolution.QueryKind));
        }

        if (resolution.QueryKind == WarehouseAssistantQueryKind.LocationCapacity)
        {
            var grouped = balances.GroupBy(x => new { x.LocationId, x.UnitCode }).ToArray();
            var capacityRows = new List<WarehouseAssistantAnalysisRow>();
            foreach (var location in locations)
            {
                var warehouse = warehouseById[location.WarehouseId];
                var locationGroups = grouped.Where(x => x.Key.LocationId == location.Id).ToArray();
                if (locationGroups.Length == 0)
                {
                    capacityRows.Add(LocationAnalysisRow(location, warehouse, "LocationCapacity"));
                    continue;
                }
                capacityRows.AddRange(locationGroups.Select(group => new WarehouseAssistantAnalysisRow(
                    "LocationCapacity", "Location", location.Id, location.Code, location.Name,
                    warehouse.WarehouseCode, warehouse.WarehouseName, location.Code, location.Name,
                    location.IsActive ? "Active" : "Inactive", group.Key.UnitCode,
                    PhysicalQuantity: group.Sum(x => x.Physical), AvailableQuantity: group.Sum(x => x.Available), ReservedQuantity: group.Sum(x => x.Reserved),
                    CapacityQuantity: string.Equals(location.CapacityUnit, group.Key.UnitCode, StringComparison.OrdinalIgnoreCase) ? location.CapacityQuantity : null,
                    CapacityUnit: location.CapacityUnit,
                    Detail: string.Equals(location.CapacityUnit, group.Key.UnitCode, StringComparison.OrdinalIgnoreCase)
                        ? "Kapasite ve stok aynı birimdedir."
                        : "Kapasite ile stok birimi farklı olduğu için doluluk oranı hesaplanmadı.",
                    Route: $"/warehouse/locations/{location.Id}")));
            }
            return AdvancedResult(resolution, "location-capacity-query",
                AdvancedMessage("AdvancedLocationCapacityFound", $"{capacityRows.Count} lokasyon/birim kapasite satırı bulundu; uyumsuz birimler birleştirilmedi."),
                capacityRows.Take(MaximumResultCount).ToArray(),
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, LocationQuery: resolution.LocationQuery, QueryKind: resolution.QueryKind));
        }

        var contentRows = balances.Take(MaximumResultCount).Select(x =>
        {
            var location = locationById[x.LocationId];
            var warehouse = warehouseById[location.WarehouseId];
            stocks.TryGetValue(x.StockId, out var stock);
            return new WarehouseAssistantAnalysisRow(
                "LocationStock", "Stock", x.StockId, stock?.ErpStockCode ?? x.StockId.ToString(), stock?.StockName ?? string.Empty,
                warehouse.WarehouseCode, warehouse.WarehouseName, location.Code, location.Name,
                UnitCode: x.UnitCode, PhysicalQuantity: x.Physical, AvailableQuantity: x.Available, ReservedQuantity: x.Reserved,
                Route: "/warehouse/stock-balances");
        }).ToArray();
        return AdvancedResult(resolution, "location-stock-query",
            contentRows.Length == 0
                ? AdvancedMessage("AdvancedLocationStockNone", "Lokasyon bulundu ancak fiziksel stok yok.")
                : AdvancedMessage("AdvancedLocationStockFound", $"Lokasyonda {contentRows.Length} stok/birim satırı bulundu."),
            contentRows,
            new WarehouseAssistantContext(null, contentRows.FirstOrDefault()?.EntityId, contentRows.FirstOrDefault()?.Code,
                WarehouseQuery: resolution.WarehouseQuery, LocationQuery: resolution.LocationQuery, QueryKind: resolution.QueryKind));
    }

    private async Task<ExecutionResult> ExecuteInventoryInsightsAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockBalances)
            return Denied(resolution.Intent, AdvancedMessage("AdvancedStockBalanceDenied", "Stok bakiyelerini görüntüleme yetkiniz bulunmuyor."));
        if (resolution.QueryKind == WarehouseAssistantQueryKind.CriticalStockUnsupported)
            return AdvancedResult(resolution, "inventory-domain-limitation",
                AdvancedMessage("AdvancedCriticalStockUnsupported", "Stok kartında veya onaylı bir politikada kritik/minimum stok eşiği bulunmadığı için kritik stok listesi üretilemiyor."),
                [], new WarehouseAssistantContext(null, null, null, QueryKind: resolution.QueryKind));

        var warehouses = await ResolveAuthorizedWarehousesAsync(actorUserId, branchCode, resolution.WarehouseQuery, ct);
        var warehouseIds = warehouses.Select(x => x.Id).ToArray();
        var balances = await unitOfWork.Repository<WarehouseStockBalance>().Query()
            .Where(x => x.BranchCode == branchCode && warehouseIds.Contains(x.WarehouseId))
            .GroupBy(x => new { x.StockId, x.UnitCode })
            .Select(x => new
            {
                x.Key.StockId,
                x.Key.UnitCode,
                Physical = x.Sum(y => y.Quantity),
                Available = x.Sum(y => y.AvailableQuantity),
                Reserved = x.Sum(y => y.ReservedQuantity)
            })
            .Take(10000)
            .ToListAsync(ct);
        var stocks = await unitOfWork.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branchCode)
            .Take(5000)
            .ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(resolution.StockGroupQuery))
        {
            var group = WarehouseAssistantTextNormalizer.Normalize(resolution.StockGroupQuery);
            stocks = stocks.Where(x => WarehouseAssistantTextNormalizer.Normalize(x.GroupCode).Contains(group, StringComparison.Ordinal)).ToList();
        }

        var balanceByStock = balances.GroupBy(x => x.StockId).ToDictionary(x => x.Key, x => x.ToArray());
        IEnumerable<WarehouseAssistantAnalysisRow> rows = stocks.SelectMany(stock =>
        {
            if (balanceByStock.TryGetValue(stock.Id, out var stockBalances))
                return stockBalances.Select(balance => StockInsightRow(stock, balance.UnitCode, balance.Physical, balance.Available, balance.Reserved));
            return [StockInsightRow(stock, stock.BaseUnitCode, 0, 0, 0)];
        });
        rows = resolution.QueryKind switch
        {
            WarehouseAssistantQueryKind.ZeroStock => rows.Where(x => x.PhysicalQuantity == 0),
            WarehouseAssistantQueryKind.NonZeroStock => rows.Where(x => x.PhysicalQuantity != 0),
            _ => rows
        };
        rows = resolution.Sort switch
        {
            WarehouseAssistantSortDirection.QuantityAscending => rows.OrderBy(x => Measure(x, resolution.StockMeasure)).ThenBy(x => x.Code),
            WarehouseAssistantSortDirection.QuantityDescending => rows.OrderByDescending(x => Measure(x, resolution.StockMeasure)).ThenBy(x => x.Code),
            _ => rows.OrderBy(x => x.Code)
        };
        var limit = Math.Clamp(resolution.Limit ?? MaximumResultCount, 1, MaximumResultCount);
        var resultRows = rows.Take(limit).ToArray();
        return AdvancedResult(resolution, "inventory-insight-query",
            AdvancedMessage("AdvancedInventoryInsightsFound", $"Yetkili depo kapsamındaki {resultRows.Length} stok/birim satırı listelendi."),
            resultRows,
            new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind, StockMeasure: resolution.StockMeasure));
    }

    private async Task<ExecutionResult> ExecuteInventoryCountAnalysisAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewInventoryCounts)
            return Denied(resolution.Intent, AdvancedMessage("AdvancedInventoryCountDenied", "Sayım emirlerini görüntüleme yetkiniz bulunmuyor."));
        if (resolution.QueryKind == WarehouseAssistantQueryKind.InventoryCountVariance && !access.CanReviewInventoryCounts)
            return Denied(resolution.Intent, AdvancedMessage("AdvancedInventoryCountReviewDenied", "Defter miktarı ve sayım farklarını görmek için sayım inceleme yetkisi gerekiyor."));

        var warehouses = await ResolveAuthorizedWarehousesAsync(actorUserId, branchCode, resolution.WarehouseQuery, ct);
        var warehouseIds = warehouses.Select(x => x.Id).ToArray();
        var warehouseById = warehouses.ToDictionary(x => x.Id);
        if (resolution.QueryKind == WarehouseAssistantQueryKind.InventoryCountVariance)
        {
            var raw = await unitOfWork.Repository<InventoryCountLine>().Query()
                .Where(x => x.BranchCode == branchCode && warehouseIds.Contains(x.WarehouseId) && x.VarianceQuantity != 0)
                .Take(5000)
                .ToListAsync(ct);
            var stockIds = raw.Select(x => x.StockId).Distinct().ToArray();
            var locationIds = raw.Select(x => x.LocationId).Distinct().ToArray();
            var stocks = await unitOfWork.Repository<StockEntity>().Query().Where(x => x.BranchCode == branchCode && stockIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var locations = await unitOfWork.Repository<WarehouseLocation>().Query().Where(x => x.BranchCode == branchCode && locationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var limit = Math.Clamp(resolution.Limit ?? MaximumResultCount, 1, MaximumResultCount);
            var ordered = resolution.Sort == WarehouseAssistantSortDirection.VarianceDescending
                ? raw.OrderByDescending(x => Math.Abs(x.VarianceQuantity))
                : raw.OrderByDescending(x => x.LastCountedAtUtc);
            var rows = ordered.Take(limit).Select(x =>
            {
                stocks.TryGetValue(x.StockId, out var stock);
                locations.TryGetValue(x.LocationId, out var location);
                var warehouse = warehouseById[x.WarehouseId];
                return new WarehouseAssistantAnalysisRow(
                    "InventoryCountVariance", "InventoryCountLine", x.Id, stock?.ErpStockCode ?? x.StockId.ToString(), stock?.StockName ?? string.Empty,
                    warehouse.WarehouseCode, warehouse.WarehouseName, location?.Code, location?.Name, x.Status.ToString(), x.UnitCode,
                    PhysicalQuantity: x.SnapshotQuantity, ActualQuantity: x.CountedQuantity, VarianceQuantity: x.VarianceQuantity,
                    ActualAtUtc: x.LastCountedAtUtc, Detail: x.DifferenceReasonCode, Route: $"/warehouse/inventory-counts/{x.HeaderId}");
            }).ToArray();
            return AdvancedResult(resolution, "inventory-count-variance-query",
                AdvancedMessage("AdvancedInventoryCountVarianceFound", $"{rows.Length} sayım farkı satırı bulundu."), rows,
                new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind));
        }

        var headers = unitOfWork.Repository<InventoryCountHeader>().Query()
            .Where(x => x.BranchCode == branchCode && warehouseIds.Contains(x.WarehouseId));
        if (resolution.StatusQuery == "Open")
            headers = headers.Where(x => x.Status != InventoryCountStatus.Completed && x.Status != InventoryCountStatus.Cancelled);
        if (resolution.ExcludeCancelled)
            headers = headers.Where(x => x.Status != InventoryCountStatus.Cancelled);
        var rawHeaders = await headers.OrderByDescending(x => x.PlannedStartUtc ?? x.CreatedDate)
            .Take(resolution.Limit ?? MaximumResultCount)
            .ToListAsync(ct);
        var headerRows = rawHeaders.Select(x =>
        {
            var warehouse = warehouseById[x.WarehouseId];
            return new WarehouseAssistantAnalysisRow(
                "InventoryCount", "InventoryCountHeader", x.Id, x.DocumentNo, x.Description ?? x.CountType.ToString(),
                warehouse.WarehouseCode, warehouse.WarehouseName, Status: x.Status.ToString(),
                PlannedQuantity: x.LineCount, ActualQuantity: x.CountedLineCount,
                PlannedAtUtc: x.PlannedStartUtc,
                ActualAtUtc: x.CompletedAtUtc,
                Detail: $"Görev {x.CompletedTaskCount}/{x.TaskCount}; fark satırı {x.VarianceLineCount}",
                Route: $"/warehouse/inventory-counts/{x.Id}");
        }).ToArray();
        return AdvancedResult(resolution, "inventory-count-query",
            AdvancedMessage("AdvancedInventoryCountFound", $"{headerRows.Length} sayım emri bulundu."), headerRows,
            new WarehouseAssistantContext(null, null, null, WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind));
    }

    private async Task<ExecutionResult> ExecuteGeneratorProductionAnalysisAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewGeneratorProduction)
            return Denied(resolution.Intent, AdvancedMessage("AdvancedGeneratorProductionDenied", "Jeneratör üretim projelerini görüntüleme yetkiniz bulunmuyor."));

        var projects = await unitOfWork.Repository<GeneratorProductionProject>().Query()
            .Where(x => x.BranchCode == branchCode)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.PlannedDeliveryAtUtc)
            .Take(1000)
            .ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(resolution.ProjectQuery))
        {
            var projectQuery = WarehouseAssistantTextNormalizer.Normalize(resolution.ProjectQuery);
            projects = projects.Where(x =>
                WarehouseAssistantTextNormalizer.Normalize(x.ProjectCode).Equals(projectQuery, StringComparison.OrdinalIgnoreCase)
                || WarehouseAssistantTextNormalizer.Normalize(x.ProjectName).Contains(projectQuery, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (resolution.StatusQuery == "Active")
            projects = projects.Where(x => x.Status is not GeneratorProjectStatus.Completed and not GeneratorProjectStatus.Cancelled).ToList();
        if (projects.Count == 0)
            return AdvancedResult(resolution, "generator-production-query",
                AdvancedMessage("AdvancedGeneratorProductionNotFound", "Eşleşen jeneratör üretim projesi bulunamadı."), [],
                new WarehouseAssistantContext(null, null, null, ProjectQuery: resolution.ProjectQuery, QueryKind: resolution.QueryKind));

        if (resolution.QueryKind is WarehouseAssistantQueryKind.ProductionProjects or WarehouseAssistantQueryKind.ProductionProjectStatus)
        {
            var rows = projects.Take(MaximumResultCount).Select(ProjectAnalysisRow).ToArray();
            return AdvancedResult(resolution, "generator-project-query",
                AdvancedMessage("AdvancedGeneratorProjectsFound", $"{rows.Length} jeneratör üretim projesi bulundu."), rows,
                new WarehouseAssistantContext(null, null, null, ProjectQuery: resolution.ProjectQuery, QueryKind: resolution.QueryKind));
        }

        var projectIds = projects.Select(x => x.Id).ToArray();
        var operations = await unitOfWork.Repository<GeneratorProductionOperation>().Query()
            .Where(x => x.BranchCode == branchCode && projectIds.Contains(x.ProjectId))
            .Take(10000)
            .ToListAsync(ct);
        var routeOperationIds = operations.Select(x => x.RouteOperationId).Distinct().ToArray();
        var stationIds = operations.Select(x => x.StationId).Distinct().ToArray();
        var routeOperations = await unitOfWork.Repository<GeneratorProductionRouteOperation>().Query()
            .Where(x => x.BranchCode == branchCode && routeOperationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var stations = await unitOfWork.Repository<GeneratorProductionStation>().Query()
            .Where(x => x.BranchCode == branchCode && stationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var quality = await unitOfWork.Repository<GeneratorProductionQualityGate>().Query()
            .Where(x => x.BranchCode == branchCode && operations.Select(y => y.Id).Contains(x.OperationId)).ToDictionaryAsync(x => x.OperationId, ct);
        var projectById = projects.ToDictionary(x => x.Id);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (resolution.QueryKind == WarehouseAssistantQueryKind.ProductionPlannedVsActual)
        {
            var rows = projects.Take(MaximumResultCount).Select(project =>
            {
                var projectOperations = operations.Where(x => x.ProjectId == project.Id).ToArray();
                var completedUnits = projectOperations.GroupBy(x => x.UnitIndex)
                    .Count(unit => unit.Any() && unit.All(x => x.Status == GeneratorOperationStatus.Completed));
                return ProjectAnalysisRow(project) with
                {
                    Category = "GeneratorPlannedVsActual",
                    PlannedQuantity = project.Quantity,
                    ActualQuantity = completedUnits,
                    Detail = "Gerçekleşen miktar, tüm operasyonları tamamlanan benzersiz ünite sayısıdır."
                };
            }).ToArray();
            return AdvancedResult(resolution, "generator-plan-actual-query",
                AdvancedMessage("AdvancedGeneratorPlanActualFound", $"{rows.Length} proje için planlanan/gerçekleşen miktar hesaplandı."), rows,
                new WarehouseAssistantContext(null, null, null, ProjectQuery: resolution.ProjectQuery, QueryKind: resolution.QueryKind));
        }

        IEnumerable<GeneratorProductionOperation> filtered = operations;
        filtered = resolution.QueryKind switch
        {
            WarehouseAssistantQueryKind.ProductionMaterialShortages => filtered.Where(x => x.HasMaterialShortage),
            WarehouseAssistantQueryKind.ProductionQualityWaiting => filtered.Where(x => quality.TryGetValue(x.Id, out var gate) && gate.Status == GeneratorQualityGateStatus.Pending),
            WarehouseAssistantQueryKind.ProductionOverdue => filtered.Where(x => x.PlannedEndAtUtc < now && x.Status is not GeneratorOperationStatus.Completed and not GeneratorOperationStatus.Cancelled),
            _ => filtered
        };
        var operationRows = filtered.OrderBy(x => x.PlannedEndAtUtc).Take(resolution.Limit ?? MaximumResultCount).Select(x =>
        {
            var project = projectById[x.ProjectId];
            routeOperations.TryGetValue(x.RouteOperationId, out var operationDefinition);
            stations.TryGetValue(x.StationId, out var station);
            quality.TryGetValue(x.Id, out var gate);
            return new WarehouseAssistantAnalysisRow(
                resolution.QueryKind == WarehouseAssistantQueryKind.ProductionMaterialShortages ? "GeneratorMaterialShortage" : "GeneratorOperation",
                "GeneratorProductionOperation", x.Id, operationDefinition?.OperationCode ?? x.Id.ToString(), operationDefinition?.OperationName ?? string.Empty,
                Status: x.Status.ToString(), PlannedQuantity: project.Quantity, ActualQuantity: x.GoodQuantity,
                PlannedAtUtc: x.PlannedEndAtUtc, ActualAtUtc: x.ActualEndAtUtc,
                Detail: string.Join("; ", new[]
                {
                    project.ProjectCode,
                    station?.Name,
                    x.HasMaterialShortage ? "Malzeme eksiği" : null,
                    gate is not null ? $"Kalite: {gate.Status}" : null,
                    x.HasProblem ? x.ProblemDescription : null
                }.Where(value => !string.IsNullOrWhiteSpace(value))),
                Route: $"/warehouse/production/generator/projects/{project.Id}");
        }).ToArray();
        return AdvancedResult(resolution, "generator-operation-query",
            AdvancedMessage("AdvancedGeneratorOperationsFound", $"{operationRows.Length} jeneratör üretim operasyonu bulundu."), operationRows,
            new WarehouseAssistantContext(null, null, null, ProjectQuery: resolution.ProjectQuery, QueryKind: resolution.QueryKind));
    }

    private ExecutionResult ExecuteNavigationHelp(WarehouseAssistantIntentResolution resolution, WarehouseAssistantAccess access)
    {
        var (titleKey, titleFallback, route, allowed, detailKey, detailFallback) = resolution.NavigationTopic switch
        {
            "stockCard" => ("NavigationStockCardTitle", "Stoklar", "/erp/stocks", access.CanViewErpMirror,
                "NavigationStockCardAnswer", "Sol menüden Entegrasyonlar → Stoklar yolunu izleyin. WMS stok kartı oluşturmaz; kartlar Netsis/ERP kaynaklıdır."),
            "goodsReceipt" => ("NavigationGoodsReceiptTitle", "Emir Oluştur", "/warehouse/goods-receipts/new", access.CanCreateGoodsReceipts,
                "NavigationGoodsReceiptAnswer", "Sol menüden Mal Kabul → Operasyon → Emir Oluştur yolunu izleyin. Bu ekran siparişe bağlı yeni mal kabul emri başlatır."),
            "warehouseTransfer" => ("NavigationWarehouseTransferTitle", "Transfer Taslağı", "/warehouse/transfers/new", access.CanCreateWarehouseTransfers,
                "NavigationWarehouseTransferAnswer", "Sol menüden Depo(Ambar) İşlemleri → Depolar Arası Transfer → Normal Transfer → Transfer Taslağı yolunu izleyin."),
            "inventoryCount" => ("NavigationInventoryCountTitle", "Sayım Yönetimi", "/warehouse/inventory-counts", access.CanViewInventoryCounts,
                "NavigationInventoryCountAnswer", "Sol menüden Depo(Ambar) İşlemleri → Depo Yönetimi → Sayım Yönetimi yolunu izleyin."),
            "stockMovements" => ("NavigationStockMovementsTitle", "Stok Hareketleri", "/warehouse/stock-movements", access.CanViewStockMovements,
                "NavigationStockMovementsAnswer", "Sol menüden Depo(Ambar) İşlemleri → Depo Yönetimi → Stok Hareketleri yolunu izleyin."),
            "generatorProjects" => ("NavigationGeneratorProjectsTitle", "Jeneratör Projeleri", "/warehouse/production/generator/projects", access.CanViewGeneratorProduction,
                "NavigationGeneratorProjectsAnswer", "Sol menüden Üretim ve Kalite → Jeneratör Üretim → Planlama → Jeneratör Projeleri yolunu izleyin."),
            _ => ("NavigationUnknownTitle", "WMS yardımı", (string?)null, false,
                "NavigationUnknownAnswer", "Bu konu için doğrulanmış bir ekran yolu bulunamadı.")
        };
        var title = AdvancedMessage(titleKey, titleFallback);
        var detail = AdvancedMessage(detailKey, detailFallback);
        var row = new WarehouseAssistantAnalysisRow(
            "Navigation", "Route", null, resolution.NavigationTopic ?? "unknown", title,
            Status: allowed ? "Allowed" : "Denied",
            Detail: allowed ? detail : $"Bu ekran için gerekli görüntüleme/oluşturma yetkiniz yok. {detail}",
            Route: allowed ? route : null);
        return AdvancedResult(
            resolution,
            "verified-navigation-catalog",
            allowed ? detail : "İlgili ekran için gerekli yetkiniz bulunmuyor; işlem yapılmadı.",
            [row],
            new WarehouseAssistantContext(null, null, null, QueryKind: resolution.QueryKind));
    }

    private async Task<IReadOnlyList<WarehouseEntity>> ResolveAuthorizedWarehousesAsync(
        long actorUserId,
        string branchCode,
        string? warehouseQuery,
        CancellationToken ct)
    {
        var access = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var query = unitOfWork.Repository<WarehouseEntity>().Query().Where(x => x.BranchCode == branchCode);
        if (access.IsRestricted) query = query.Where(x => access.WarehouseIds.Contains(x.Id));
        var warehouses = await query.OrderBy(x => x.WarehouseCode).Take(1000).ToListAsync(ct);
        if (string.IsNullOrWhiteSpace(warehouseQuery)) return warehouses;

        var normalized = WarehouseAssistantTextNormalizer.Normalize(warehouseQuery);
        return warehouses.Where(x => x.WarehouseCode.ToString().Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || WarehouseAssistantTextNormalizer.Normalize(x.WarehouseName).Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private ExecutionResult AdvancedResult(
        WarehouseAssistantIntentResolution resolution,
        string tool,
        string answer,
        IReadOnlyList<WarehouseAssistantAnalysisRow> rows,
        WarehouseAssistantContext context,
        IReadOnlyList<WarehouseAssistantSummaryMetricRow>? metrics = null) => new(
        resolution.Intent,
        "authorized-branch-warehouse-scope",
        tool,
        answer,
        [], [], [], [], null, [], [],
        context,
        [],
        SummaryMetrics: metrics ?? [],
        AnalysisRows: rows);

    private string AdvancedMessage(string key, string fallback)
    {
        if (localizer is null) return fallback;
        var localized = localizer[key];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }

    private static WarehouseAssistantAnalysisRow LocationAnalysisRow(WarehouseLocation location, WarehouseEntity warehouse, string category) => new(
        category, "Location", location.Id, location.Code, location.Name,
        warehouse.WarehouseCode, warehouse.WarehouseName, location.Code, location.Name,
        location.IsActive ? "Active" : "Inactive",
        CapacityQuantity: location.CapacityQuantity,
        CapacityUnit: location.CapacityUnit,
        Detail: location.IsQuarantine ? "Karantina lokasyonu" : location.LocationType,
        Route: "/warehouse/locations");

    private static WarehouseAssistantAnalysisRow StockInsightRow(StockEntity stock, string unitCode, decimal physical, decimal available, decimal reserved) => new(
        "InventoryInsight", "Stock", stock.Id, stock.ErpStockCode, stock.StockName,
        Status: physical == 0 ? "Zero" : "InStock", UnitCode: unitCode,
        PhysicalQuantity: physical, AvailableQuantity: available, ReservedQuantity: reserved,
        Detail: string.IsNullOrWhiteSpace(stock.GroupCode) ? null : $"Grup: {stock.GroupCode}",
        Route: "/erp/stocks");

    private static decimal Measure(WarehouseAssistantAnalysisRow row, WarehouseAssistantStockMeasure? measure) => measure switch
    {
        WarehouseAssistantStockMeasure.Available => row.AvailableQuantity ?? 0,
        WarehouseAssistantStockMeasure.Reserved => row.ReservedQuantity ?? 0,
        _ => row.PhysicalQuantity ?? 0
    };

    private static WarehouseAssistantAnalysisRow ProjectAnalysisRow(GeneratorProductionProject project) => new(
        "GeneratorProject", "GeneratorProductionProject", project.Id, project.ProjectCode, project.ProjectName,
        Status: project.Status.ToString(), PlannedQuantity: project.Quantity,
        PlannedAtUtc: project.PlannedDeliveryAtUtc,
        Detail: string.Join("; ", new[] { project.GeneratorType, project.SerialNumber, project.CustomerNameSnapshot }.Where(x => !string.IsNullOrWhiteSpace(x))),
        Route: $"/warehouse/production/generator/projects/{project.Id}");
}
