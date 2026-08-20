using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferSourceLocationExclusions
{
    internal static HashSet<long> FromTransfer(CreateWarehouseTransferDraftRequest transfer)
    {
        var excluded = new HashSet<long>();
        AddIfHasValue(excluded, transfer.SourceStagingLocationId);
        AddIfHasValue(excluded, transfer.TargetPutawayLocationId);
        foreach (var line in transfer.Lines)
            AddIfHasValue(excluded, line.DefaultTargetLocationId);
        return excluded;
    }

    internal static async Task<HashSet<long>> FromHeaderAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        IEnumerable<WarehouseTransferLine> lines,
        CancellationToken ct)
    {
        var excluded = new HashSet<long>();
        AddIfHasValue(excluded, header.SourceStagingLocationId);
        AddIfHasValue(excluded, header.TargetPutawayLocationId);
        foreach (var line in lines)
            AddIfHasValue(excluded, line.DefaultTargetLocationId);

        var warehouseIds = new[] { header.SourceWarehouseId, header.TargetWarehouseId }.Distinct().ToArray();
        var defaults = await uow.Repository<WarehouseEntity>().Query()
            .Where(x => warehouseIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.DefaultProductionTransferLocationId,
                x.ProductionPickingStagingLocationId
            })
            .ToListAsync(ct);

        long? sourcePickingStagingId = null;
        foreach (var row in defaults)
        {
            if (row.ProductionPickingStagingLocationId.HasValue)
                excluded.Add(row.ProductionPickingStagingLocationId.Value);
            if (row.Id == header.SourceWarehouseId)
                sourcePickingStagingId = row.ProductionPickingStagingLocationId;
            if (row.Id == header.TargetWarehouseId && row.DefaultProductionTransferLocationId.HasValue)
                excluded.Add(row.DefaultProductionTransferLocationId.Value);
            if (row.Id == header.SourceWarehouseId && row.DefaultProductionTransferLocationId.HasValue)
                excluded.Add(row.DefaultProductionTransferLocationId.Value);
        }

        await KeepPickableSourceLocationsAsync(
            uow, header.SourceWarehouseId, sourcePickingStagingId, excluded, ct);
        return excluded;
    }

    internal static async Task<HashSet<long>> FromTransferAsync(
        IUnitOfWork uow,
        CreateWarehouseTransferDraftRequest transfer,
        CancellationToken ct)
    {
        var excluded = FromTransfer(transfer);
        var warehouseIds = new[] { transfer.SourceWarehouseId, transfer.TargetWarehouseId }.Distinct().ToArray();
        var defaults = await uow.Repository<WarehouseEntity>().Query()
            .Where(x => warehouseIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.DefaultProductionTransferLocationId,
                x.ProductionPickingStagingLocationId
            })
            .ToListAsync(ct);

        long? sourcePickingStagingId = null;
        foreach (var row in defaults)
        {
            if (row.ProductionPickingStagingLocationId.HasValue)
                excluded.Add(row.ProductionPickingStagingLocationId.Value);
            if (row.Id == transfer.SourceWarehouseId)
                sourcePickingStagingId = row.ProductionPickingStagingLocationId;
            if (row.Id == transfer.TargetWarehouseId && row.DefaultProductionTransferLocationId.HasValue)
                excluded.Add(row.DefaultProductionTransferLocationId.Value);
            if (row.Id == transfer.SourceWarehouseId && row.DefaultProductionTransferLocationId.HasValue)
                excluded.Add(row.DefaultProductionTransferLocationId.Value);
        }

        await KeepPickableSourceLocationsAsync(
            uow, transfer.SourceWarehouseId, sourcePickingStagingId, excluded, ct);
        return excluded;
    }

    /// <summary>
    /// Raflı depoda toplanabilir mal kabul / varsayılan üretim rafı kaynak rotaya girer.
    /// Toplananların gittiği sanal raf her zaman dışarıda kalır.
    /// </summary>
    internal static async Task KeepPickableSourceLocationsAsync(
        IUnitOfWork uow,
        long sourceWarehouseId,
        long? pickingStagingLocationId,
        HashSet<long> excluded,
        CancellationToken ct)
    {
        var ids = excluded.ToArray();
        if (ids.Length == 0) return;

        var pickableIds = await uow.Repository<WarehouseLocation>().Query()
            .Where(x => x.WarehouseId == sourceWarehouseId
                && ids.Contains(x.Id)
                && x.IsActive
                && x.IsPickable
                && !x.IsQuarantine)
            .Select(x => x.Id)
            .ToListAsync(ct);

        foreach (var id in pickableIds)
        {
            if (pickingStagingLocationId == id) continue;
            excluded.Remove(id);
        }
    }

    private static void AddIfHasValue(ISet<long> excluded, long? locationId)
    {
        if (locationId.HasValue) excluded.Add(locationId.Value);
    }
}
