using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.StockBalance.Application;

public sealed partial class StockBalanceService
{
    private const int InventoryLookupLineLimit = 60;

    public async Task<WarehouseInventoryLookup> GetWarehouseInventoryLookupAsync(
        long warehouseId,
        CancellationToken cancellationToken = default)
    {
        var warehouse = await WarehouseDefinitions.Query()
            .FirstOrDefaultAsync(x => x.Id == warehouseId, cancellationToken)
            ?? throw AppException.NotFound("Depo bulunamadı.");

        var balances = Locations.Query().Where(x => x.WarehouseId == warehouseId);
        var quantity = await balances.SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0;
        var reserved = await balances.SumAsync(x => (decimal?)x.ReservedQuantity, cancellationToken) ?? 0;
        var available = await balances.SumAsync(x => (decimal?)x.AvailableQuantity, cancellationToken) ?? 0;
        var stockCount = await balances.Select(x => x.StockId).Distinct().CountAsync(cancellationToken);
        var locationCount = await balances.Select(x => x.LocationId).Distinct().CountAsync(cancellationToken);
        var lineCount = await balances.CountAsync(cancellationToken);
        var lines = await BuildLocationRows(balances.OrderByDescending(x => x.AvailableQuantity).ThenBy(x => x.LocationId))
            .Take(InventoryLookupLineLimit)
            .ToListAsync(cancellationToken);

        return new WarehouseInventoryLookup(
            warehouse.Id,
            warehouse.WarehouseCode,
            warehouse.WarehouseName,
            warehouse.BranchCode,
            quantity,
            reserved,
            available,
            stockCount,
            locationCount,
            lineCount > lines.Count,
            lines);
    }

    public async Task<LocationInventoryLookup> GetLocationInventoryLookupAsync(
        long locationId,
        CancellationToken cancellationToken = default)
    {
        var location = await (
            from loc in LocationDefinitions.Query()
            join warehouse in WarehouseDefinitions.Query() on loc.WarehouseId equals warehouse.Id
            where loc.Id == locationId
            select new { Location = loc, Warehouse = warehouse })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw AppException.NotFound("Raf bulunamadı.");

        var balances = Locations.Query().Where(x => x.LocationId == locationId);
        var quantity = await balances.SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0;
        var reserved = await balances.SumAsync(x => (decimal?)x.ReservedQuantity, cancellationToken) ?? 0;
        var available = await balances.SumAsync(x => (decimal?)x.AvailableQuantity, cancellationToken) ?? 0;
        var stockCount = await balances.Select(x => x.StockId).Distinct().CountAsync(cancellationToken);
        var lineCount = await balances.CountAsync(cancellationToken);
        var lines = await BuildLocationRows(balances.OrderByDescending(x => x.AvailableQuantity).ThenBy(x => x.StockId))
            .Take(InventoryLookupLineLimit)
            .ToListAsync(cancellationToken);

        return new LocationInventoryLookup(
            location.Location.Id,
            location.Location.Code,
            location.Location.Name,
            location.Location.LocationType,
            location.Warehouse.Id,
            location.Warehouse.WarehouseCode,
            location.Warehouse.WarehouseName,
            location.Location.BranchCode,
            quantity,
            reserved,
            available,
            stockCount,
            lineCount > lines.Count,
            lines);
    }

    public async Task<SerialInventoryLookup> GetSerialInventoryLookupAsync(
        long serialBalanceId,
        CancellationToken cancellationToken = default)
    {
        var page = await GetSerialBalancesAsync(new PagedRequest
        {
            PageNumber = 1,
            PageSize = 1,
            Filters = [new AdvancedFilterRequest("id", "equals", serialBalanceId.ToString())],
        }, cancellationToken);
        var balance = page.Items.FirstOrDefault()
            ?? throw AppException.NotFound("Seri bakiyesi bulunamadı.");
        var movements = await GetSerialMovementHistoryAsync(
            serialBalanceId,
            new PagedRequest { PageNumber = 1, PageSize = 20 },
            cancellationToken);
        return new SerialInventoryLookup(balance, movements.Items);
    }

    public async Task<LotInventoryLookup> GetLotInventoryLookupAsync(
        string? lotNo,
        CancellationToken cancellationToken = default)
    {
        var term = lotNo?.Trim() ?? "";
        if (term.Length == 0)
            throw AppException.BadRequest("Lot numarası zorunludur.");

        var balances = Locations.Query().Where(x => x.LotNo == term);
        if (!await balances.AnyAsync(cancellationToken))
            throw AppException.NotFound("Lot bakiyesi bulunamadı.");

        var quantity = await balances.SumAsync(x => (decimal?)x.Quantity, cancellationToken) ?? 0;
        var reserved = await balances.SumAsync(x => (decimal?)x.ReservedQuantity, cancellationToken) ?? 0;
        var available = await balances.SumAsync(x => (decimal?)x.AvailableQuantity, cancellationToken) ?? 0;
        var stockCount = await balances.Select(x => x.StockId).Distinct().CountAsync(cancellationToken);
        var locationCount = await balances.Select(x => x.LocationId).Distinct().CountAsync(cancellationToken);
        var lineCount = await balances.CountAsync(cancellationToken);
        var lines = await BuildLocationRows(balances.OrderByDescending(x => x.AvailableQuantity).ThenBy(x => x.LocationId))
            .Take(InventoryLookupLineLimit)
            .ToListAsync(cancellationToken);

        return new LotInventoryLookup(
            term,
            quantity,
            reserved,
            available,
            stockCount,
            locationCount,
            lineCount > lines.Count,
            lines);
    }
}
