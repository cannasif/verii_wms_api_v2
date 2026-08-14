using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

/// <summary>
/// Rafsız depo tespiti: aktif + LocationType != Virtual + (IsPickable veya IsPutaway) olan
/// gerçek raf sayısı sıfırsa depo rafsızdır. Elle işaretlenen bir alan değildir.
/// </summary>
internal static class ProductionTransferWarehouseRacklessSupport
{
    internal static bool IsRealRackLocation(WarehouseLocation location) =>
        location.IsActive
        && !string.Equals(location.LocationType, LocationTypes.Receiving, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(location.LocationType, LocationTypes.Staging, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(location.LocationType, LocationTypes.Shipping, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(location.LocationType, LocationTypes.Virtual, StringComparison.OrdinalIgnoreCase)
        && (location.IsPickable || location.IsPutaway);

    internal static IQueryable<WarehouseLocation> RealRackLocations(IQueryable<WarehouseLocation> locations) =>
        locations.Where(x =>
            x.IsActive
            && x.LocationType != LocationTypes.Receiving
            && x.LocationType != LocationTypes.Staging
            && x.LocationType != LocationTypes.Shipping
            && x.LocationType != LocationTypes.Virtual
            && (x.IsPickable || x.IsPutaway));

    internal static async Task<bool> IsRacklessAsync(
        IUnitOfWork uow,
        long warehouseId,
        CancellationToken ct = default)
    {
        var hasRealRack = await RealRackLocations(uow.Repository<WarehouseLocation>().Query())
            .AnyAsync(x => x.WarehouseId == warehouseId, ct);
        return !hasRealRack;
    }

    internal static async Task<IReadOnlyDictionary<long, bool>> GetRacklessFlagsAsync(
        IUnitOfWork uow,
        IReadOnlyCollection<long> warehouseIds,
        CancellationToken ct = default)
    {
        if (warehouseIds.Count == 0)
            return new Dictionary<long, bool>();

        var distinctIds = warehouseIds.Distinct().ToArray();
        var warehousesWithRealRacks = await RealRackLocations(uow.Repository<WarehouseLocation>().Query())
            .Where(x => distinctIds.Contains(x.WarehouseId))
            .Select(x => x.WarehouseId)
            .Distinct()
            .ToListAsync(ct);

        var withRacks = warehousesWithRealRacks.ToHashSet();
        return distinctIds.ToDictionary(id => id, id => !withRacks.Contains(id));
    }

    /// <summary>
    /// Rafsız depoda hedef/kaynak rolündeki tek sanal raf: DefaultProductionTransferLocationId.
    /// Raflı depoda null döner.
    /// </summary>
    internal static async Task<long?> GetRacklessTargetLocationIdAsync(
        IUnitOfWork uow,
        long warehouseId,
        CancellationToken ct = default)
    {
        if (!await IsRacklessAsync(uow, warehouseId, ct))
            return null;

        return await uow.Repository<WarehouseEntity>().Query()
            .Where(x => x.Id == warehouseId)
            .Select(x => x.DefaultProductionTransferLocationId)
            .SingleOrDefaultAsync(ct);
    }
}
