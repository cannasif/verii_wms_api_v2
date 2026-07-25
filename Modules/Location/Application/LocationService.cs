using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Location.Localization;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Location.Application;

public sealed partial class LocationService(IUnitOfWork unitOfWork, IAuditLogWriter audit, IStringLocalizer<LocationResource> localizer) : ILocationService
{
    private IGenericRepository<WarehouseLocation> Locations => unitOfWork.Repository<WarehouseLocation>();
    private IGenericRepository<WarehouseEntity> Warehouses => unitOfWork.Repository<WarehouseEntity>();

    public async Task<PagedResponse<LocationGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var search = request.Search?.Trim();
        var query = BuildGridQuery().Where(x => string.IsNullOrWhiteSpace(search)
            || x.Code.Contains(search) || x.Name.Contains(search) || x.WarehouseName.Contains(search)
            || (x.Barcode != null && x.Barcode.Contains(search)) || (x.ZoneCode != null && x.ZoneCode.Contains(search)));
        query = query.ApplyAdvancedFilters(request).ApplySort(request, nameof(LocationGridRow.Code));
        return await query.ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<LocationGridRow> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        await BuildGridQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw AppException.NotFound(Message(LocationMessageKeys.LocationNotFound));

    public async Task<IReadOnlyList<LocationLookupRow>> GetLookupAsync(long warehouseId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = Locations.Query().Where(x => x.WarehouseId == warehouseId);
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.LocationType).ThenBy(x => x.Code)
            .Select(x => new LocationLookupRow(x.Id, x.WarehouseId, x.ParentLocationId, x.Code, x.Name, x.LocationType, x.Barcode))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PutawayLocationSuggestion>> GetPutawaySuggestionsAsync(
        long warehouseId,
        long? stockId,
        string? stockCode,
        long? yapCodeId,
        decimal quantity,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId <= 0 || quantity <= 0)
            throw AppException.BadRequest("Depo ve kabul miktarı zorunludur.");

        var warehouse = await Warehouses.Query()
            .Where(x => x.Id == warehouseId)
            .Select(x => new { x.Id, x.BranchCode })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("Depo bulunamadı.");

        var normalizedStockCode = Normalize(stockCode)?.ToUpperInvariant();
        var resolvedStockId = stockId;
        if (!resolvedStockId.HasValue && normalizedStockCode is not null)
            resolvedStockId = await unitOfWork.Repository<StockEntity>().Query()
                .Where(x => x.BranchCode == warehouse.BranchCode && x.ErpStockCode == normalizedStockCode)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        if (!resolvedStockId.HasValue)
            throw AppException.BadRequest("Raf önerisi için geçerli bir stok zorunludur.");

        var candidates = await Locations.Query()
            .Where(x => x.WarehouseId == warehouseId
                && x.IsActive
                && x.IsPutaway
                && !x.IsQuarantine
                && x.LocationType != LocationTypes.Receiving
                && x.LocationType != LocationTypes.Staging
                && x.LocationType != LocationTypes.Shipping
                && x.LocationType != LocationTypes.Virtual)
            .Select(x => new
            {
                x.Id, x.WarehouseId, x.Code, x.Name, x.LocationType, x.ZoneCode,
                x.CapacityQuantity, x.AllowMixedStock
            })
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0) return [];

        var candidateIds = candidates.Select(x => x.Id).ToArray();
        var balances = await unitOfWork.Repository<LocationStockBalance>().Query()
            .Where(x => candidateIds.Contains(x.LocationId) && x.Quantity != 0)
            .GroupBy(x => new { x.LocationId, x.StockId, x.YapCodeId })
            .Select(x => new
            {
                x.Key.LocationId,
                x.Key.StockId,
                x.Key.YapCodeId,
                Quantity = x.Sum(y => y.Quantity),
                AvailableQuantity = x.Sum(y => y.AvailableQuantity)
            })
            .ToListAsync(cancellationToken);

        var occupancy = balances.GroupBy(x => x.LocationId).ToDictionary(
            x => x.Key,
            x => new
            {
                Total = x.Sum(y => y.Quantity),
                StockIds = x.Select(y => y.StockId).Distinct().ToHashSet()
            });

        return candidates
            .Select(location =>
            {
                occupancy.TryGetValue(location.Id, out var occupied);
                var stockBalances = balances.Where(x => x.LocationId == location.Id && x.StockId == resolvedStockId.Value).ToList();
                var exactBalances = yapCodeId.HasValue
                    ? stockBalances.Where(x => x.YapCodeId == yapCodeId).ToList()
                    : stockBalances;
                var currentQuantity = exactBalances.Sum(x => x.Quantity);
                var currentAvailable = exactBalances.Sum(x => x.AvailableQuantity);
                var totalQuantity = occupied?.Total ?? 0;
                var containsStock = stockBalances.Count > 0;
                var containsOtherStock = occupied?.StockIds.Any(x => x != resolvedStockId.Value) == true;
                var remainingCapacity = location.CapacityQuantity.HasValue
                    ? location.CapacityQuantity.Value - totalQuantity
                    : (decimal?)null;
                var eligible = (!containsOtherStock || location.AllowMixedStock)
                    && (!remainingCapacity.HasValue || remainingCapacity.Value >= quantity);
                if (!eligible) return null;

                var isEmpty = totalQuantity == 0;
                var exactYap = yapCodeId.HasValue && exactBalances.Count > 0;
                var score = (containsStock ? 1000 : 0)
                    + (exactYap ? 250 : 0)
                    + (isEmpty ? 100 : 0)
                    + (location.LocationType == LocationTypes.Cell ? 60 : location.LocationType == LocationTypes.Shelf ? 40 : 20)
                    + (remainingCapacity.HasValue ? Math.Max(0, 50 - (int)Math.Min(50, remainingCapacity.Value)) : 10);
                var reason = containsStock
                    ? exactYap ? "Aynı stok ve YAP bu rafta mevcut." : "Aynı stok bu rafta mevcut; konsolidasyon için önerildi."
                    : isEmpty ? "Boş ve mal yerleştirmeye uygun raf." : "Karışık stoğa izin veren uygun raf.";
                return new PutawayLocationSuggestion(
                    location.Id, location.WarehouseId, location.Code, location.Name, location.LocationType,
                    location.ZoneCode, currentQuantity, currentAvailable, totalQuantity, location.CapacityQuantity,
                    remainingCapacity, containsStock, isEmpty, score, reason);
            })
            .Where(x => x is not null)
            .OrderByDescending(x => x!.Score)
            .ThenBy(x => x!.Code)
            .Take(Math.Clamp(limit, 1, 20))
            .Cast<PutawayLocationSuggestion>()
            .ToList();
    }

    public async Task<LocationStats> GetStatsAsync(CancellationToken cancellationToken = default) => new(
        await Locations.CountAsync(cancellationToken: cancellationToken),
        await Locations.CountAsync(x => x.IsActive, cancellationToken),
        await Locations.CountAsync(x => x.IsActive && x.IsPickable, cancellationToken),
        await Locations.CountAsync(x => x.IsActive && x.IsQuarantine, cancellationToken));

    public async Task<long> CreateAsync(LocationUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ValidateAsync(request, null, cancellationToken);
        var entity = new WarehouseLocation();
        Apply(entity, request, resolved);
        entity.BranchCode = resolved.BranchCode;
        await Locations.AddAsync(entity, cancellationToken);
        await SaveAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("location.create", "WarehouseLocation", entity.Id.ToString(), "Succeeded", "location", NewValues: Snapshot(entity), ChangedFields: Fields), cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(long id, LocationUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await Locations.FindByIdAsync(id, tracking: true, cancellationToken)
            ?? throw AppException.NotFound(Message(LocationMessageKeys.LocationNotFound));
        if (entity.WarehouseId != request.WarehouseId && await Locations.AnyAsync(x => x.ParentLocationId == id, cancellationToken))
            throw AppException.Conflict(Message(LocationMessageKeys.WarehouseChangeBlocked));

        var resolved = await ValidateAsync(request, id, cancellationToken);
        var oldValues = Snapshot(entity);
        Apply(entity, request, resolved);
        entity.BranchCode = resolved.BranchCode;
        await SaveAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("location.update", "WarehouseLocation", id.ToString(), "Succeeded", "location", OldValues: oldValues, NewValues: Snapshot(entity), ChangedFields: Fields), cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await Locations.FindByIdAsync(id, tracking: true, cancellationToken)
            ?? throw AppException.NotFound(Message(LocationMessageKeys.LocationNotFound));
        if (await Locations.AnyAsync(x => x.ParentLocationId == id, cancellationToken))
            throw AppException.Conflict(Message(LocationMessageKeys.DeleteHasChildren));

        var oldValues = Snapshot(entity);
        entity.IsActive = false;
        await Locations.SoftDeleteAsync(id, cancellationToken);
        await SaveAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("location.delete", "WarehouseLocation", id.ToString(), "Succeeded", "location", OldValues: oldValues, ChangedFields: ["IsDeleted", "IsActive"]), cancellationToken);
    }

    private IQueryable<LocationGridRow> BuildGridQuery()
    {
        var locations = Locations.Query();
        var warehouses = Warehouses.Query();
        return from location in locations
               join warehouse in warehouses on location.WarehouseId equals warehouse.Id
               join parentLocation in locations on location.ParentLocationId equals parentLocation.Id into parentLocations
               from parent in parentLocations.DefaultIfEmpty()
               select new LocationGridRow
               {
                   Id = location.Id,
                   BranchCode = location.BranchCode,
                   WarehouseId = location.WarehouseId,
                   WarehouseCode = warehouse.WarehouseCode,
                   WarehouseName = warehouse.WarehouseName,
                   ParentLocationId = location.ParentLocationId,
                   ParentCode = parent == null ? null : parent.Code,
                   Code = location.Code,
                   Name = location.Name,
                   LocationType = location.LocationType,
                   BarcodeEntryMode = location.BarcodeEntryMode,
                   Barcode = location.Barcode,
                   ZoneCode = location.ZoneCode,
                   AisleNo = location.AisleNo,
                   RackNo = location.RackNo,
                   LevelNo = location.LevelNo,
                   BinNo = location.BinNo,
                   CapacityQuantity = location.CapacityQuantity,
                   CapacityWeight = location.CapacityWeight,
                   CapacityVolume = location.CapacityVolume,
                   CapacityUnit = location.CapacityUnit,
                   AllowMixedStock = location.AllowMixedStock,
                   AllowMixedLot = location.AllowMixedLot,
                   AllowMixedStatus = location.AllowMixedStatus,
                   AllowCycleCount = location.AllowCycleCount,
                   IsPickable = location.IsPickable,
                   IsPutaway = location.IsPutaway,
                   IsQuarantine = location.IsQuarantine,
                   IsActive = location.IsActive,
                   Description = location.Description,
                   CreatedBy = location.CreatedBy,
                   CreatedDate = location.CreatedDate,
                   UpdatedBy = location.UpdatedBy,
                   UpdatedDate = location.UpdatedDate
               };
    }

    private async Task<ResolvedLocation> ValidateAsync(LocationUpsertRequest request, long? currentId, CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        if (!CodePattern().IsMatch(code)) throw AppException.BadRequest(Message(LocationMessageKeys.InvalidCode));
        if (name.Length is < 2 or > 150) throw AppException.BadRequest(Message(LocationMessageKeys.InvalidName));
        if (request.Description?.Length > 500 || request.ZoneCode?.Length > 50 || request.CapacityUnit?.Length > 20) throw AppException.BadRequest(Message(LocationMessageKeys.InvalidFieldLengths));
        if (request.AisleNo is < 0 or > 9999 || request.RackNo is < 0 or > 9999 || request.LevelNo is < 0 or > 9999 || request.BinNo is < 0 or > 9999) throw AppException.BadRequest(Message(LocationMessageKeys.InvalidAddressNumber));
        if (request.CapacityQuantity is < 0 || request.CapacityWeight is < 0 || request.CapacityVolume is < 0) throw AppException.BadRequest(Message(LocationMessageKeys.NegativeCapacity));
        if ((request.CapacityQuantity.HasValue || request.CapacityWeight.HasValue || request.CapacityVolume.HasValue) && string.IsNullOrWhiteSpace(request.CapacityUnit)) throw AppException.BadRequest(Message(LocationMessageKeys.CapacityUnitRequired));
        if (request.IsQuarantine && request.IsPickable) throw AppException.BadRequest(Message(LocationMessageKeys.QuarantineCannotBePickable));

        var locationType = NormalizeAllowed(request.LocationType, LocationTypes.All, Message(LocationMessageKeys.InvalidLocationType));
        var barcodeMode = NormalizeAllowed(request.BarcodeEntryMode, BarcodeEntryModes.All, Message(LocationMessageKeys.InvalidBarcodeMode));
        var warehouse = await Warehouses.Query().Where(x => x.Id == request.WarehouseId)
            .Select(x => new { x.Id, x.BranchCode, x.WarehouseCode }).FirstOrDefaultAsync(cancellationToken)
            ?? throw AppException.BadRequest(Message(LocationMessageKeys.WarehouseNotFound));

        if (await Locations.AnyAsync(x => x.Id != currentId && x.WarehouseId == request.WarehouseId && x.Code == code, cancellationToken))
            throw AppException.Conflict(Message(LocationMessageKeys.DuplicateCode));

        await ValidateHierarchyAsync(request.WarehouseId, request.ParentLocationId, locationType, currentId, cancellationToken);
        var barcode = barcodeMode == BarcodeEntryModes.Manual
            ? Normalize(request.Barcode) ?? throw AppException.BadRequest(Message(LocationMessageKeys.ManualBarcodeRequired))
            : $"LOC-{warehouse.BranchCode}-{warehouse.WarehouseCode}-{code}";
        if (barcode.Length > 100) throw AppException.BadRequest(Message(LocationMessageKeys.GeneratedBarcodeTooLong));
        if (await Locations.AnyAsync(x => x.Id != currentId && x.Barcode == barcode, cancellationToken))
            throw AppException.Conflict(Message(LocationMessageKeys.DuplicateBarcode));

        return new ResolvedLocation(warehouse.BranchCode, code, name, locationType, barcodeMode, barcode);
    }

    private async Task ValidateHierarchyAsync(long warehouseId, long? parentId, string locationType, long? currentId, CancellationToken cancellationToken)
    {
        var rows = await Locations.Query().Where(x => x.WarehouseId == warehouseId)
            .Select(x => new { x.Id, x.ParentLocationId, x.LocationType }).ToListAsync(cancellationToken);
        var byId = rows.ToDictionary(x => x.Id);
        if (!parentId.HasValue)
        {
            if (locationType is LocationTypes.Aisle or LocationTypes.Rack or LocationTypes.Shelf or LocationTypes.Cell)
                throw AppException.BadRequest(Message(LocationMessageKeys.ParentRequired));
            return;
        }

        if (!byId.TryGetValue(parentId.Value, out var parent)) throw AppException.BadRequest(Message(LocationMessageKeys.ParentNotFoundInWarehouse));
        if (!IsParentAllowed(locationType, parent.LocationType)) throw AppException.BadRequest(localizer[LocationMessageKeys.InvalidParentType, locationType, parent.LocationType].Value);

        var visited = new HashSet<long>();
        long? cursor = parentId;
        while (cursor.HasValue)
        {
            if (cursor == currentId || !visited.Add(cursor.Value)) throw AppException.Conflict(Message(LocationMessageKeys.HierarchyCycle));
            cursor = byId.TryGetValue(cursor.Value, out var node) ? node.ParentLocationId : null;
        }
    }

    private static bool IsParentAllowed(string child, string parent) => child switch
    {
        LocationTypes.Aisle => parent == LocationTypes.Zone,
        LocationTypes.Rack => parent is LocationTypes.Zone or LocationTypes.Aisle,
        LocationTypes.Shelf => parent == LocationTypes.Rack,
        LocationTypes.Cell => parent is LocationTypes.Rack or LocationTypes.Shelf,
        LocationTypes.Receiving or LocationTypes.Staging or LocationTypes.Shipping or LocationTypes.Quarantine or LocationTypes.Virtual => parent == LocationTypes.Zone,
        _ => false
    };

    private static void Apply(WarehouseLocation entity, LocationUpsertRequest request, ResolvedLocation resolved)
    {
        entity.WarehouseId = request.WarehouseId; entity.ParentLocationId = request.ParentLocationId;
        entity.Code = resolved.Code; entity.Name = resolved.Name; entity.LocationType = resolved.LocationType;
        entity.BarcodeEntryMode = resolved.BarcodeEntryMode; entity.Barcode = resolved.Barcode;
        entity.ZoneCode = Normalize(request.ZoneCode); entity.AisleNo = request.AisleNo; entity.RackNo = request.RackNo;
        entity.LevelNo = request.LevelNo; entity.BinNo = request.BinNo; entity.CapacityQuantity = request.CapacityQuantity;
        entity.CapacityWeight = request.CapacityWeight; entity.CapacityVolume = request.CapacityVolume;
        entity.CapacityUnit = Normalize(request.CapacityUnit)?.ToUpperInvariant(); entity.AllowMixedStock = request.AllowMixedStock;
        entity.AllowMixedLot = request.AllowMixedLot; entity.AllowMixedStatus = request.AllowMixedStatus;
        entity.AllowCycleCount = request.AllowCycleCount; entity.IsPickable = request.IsPickable; entity.IsPutaway = request.IsPutaway;
        entity.IsQuarantine = request.IsQuarantine; entity.IsActive = request.IsActive; entity.Description = Normalize(request.Description);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict(Message(LocationMessageKeys.ConcurrencyConflict)); }
    }

    private static string NormalizeAllowed(string? value, IReadOnlySet<string> allowed, string error)
    {
        var match = allowed.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw AppException.BadRequest(error);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private string Message(string key) => localizer[key].Value;
    private static object Snapshot(WarehouseLocation x) => new { x.WarehouseId, x.ParentLocationId, x.Code, x.Name, x.LocationType, x.BarcodeEntryMode, x.Barcode, x.ZoneCode, x.AisleNo, x.RackNo, x.LevelNo, x.BinNo, x.CapacityQuantity, x.CapacityWeight, x.CapacityVolume, x.CapacityUnit, x.AllowMixedStock, x.AllowMixedLot, x.AllowMixedStatus, x.AllowCycleCount, x.IsPickable, x.IsPutaway, x.IsQuarantine, x.IsActive, x.Description };
    private static readonly string[] Fields = ["WarehouseId", "ParentLocationId", "Code", "Name", "LocationType", "BarcodeEntryMode", "Barcode", "ZoneCode", "AisleNo", "RackNo", "LevelNo", "BinNo", "CapacityQuantity", "CapacityWeight", "CapacityVolume", "CapacityUnit", "AllowMixedStock", "AllowMixedLot", "AllowMixedStatus", "AllowCycleCount", "IsPickable", "IsPutaway", "IsQuarantine", "IsActive", "Description"];
    private sealed record ResolvedLocation(string BranchCode, string Code, string Name, string LocationType, string BarcodeEntryMode, string Barcode);

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
