using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using WarehouseEntity=verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferSourceLocationExclusions
{
    internal static HashSet<long> FromTransfer(CreateWarehouseTransferDraftRequest transfer)
    {
        var excluded=new HashSet<long>();
        AddIfHasValue(excluded,transfer.SourceStagingLocationId);
        AddIfHasValue(excluded,transfer.TargetPutawayLocationId);
        foreach(var line in transfer.Lines)
            AddIfHasValue(excluded,line.DefaultTargetLocationId);
        return excluded;
    }

    internal static async Task<HashSet<long>> FromHeaderAsync(
        IUnitOfWork uow,WarehouseTransferHeader header,IEnumerable<WarehouseTransferLine> lines,CancellationToken ct)
    {
        var excluded=new HashSet<long>();
        AddIfHasValue(excluded,header.SourceStagingLocationId);
        AddIfHasValue(excluded,header.TargetPutawayLocationId);
        foreach(var line in lines)
            AddIfHasValue(excluded,line.DefaultTargetLocationId);

        var warehouseIds=new[]{header.SourceWarehouseId,header.TargetWarehouseId}.Distinct().ToArray();
        var defaults=await uow.Repository<WarehouseEntity>().Query()
            .Where(x=>warehouseIds.Contains(x.Id))
            .Select(x=>new{x.DefaultProductionTransferLocationId,x.ProductionPickingStagingLocationId})
            .ToListAsync(ct);
        foreach(var row in defaults)
        {
            if(row.DefaultProductionTransferLocationId.HasValue)
                excluded.Add(row.DefaultProductionTransferLocationId.Value);
            if(row.ProductionPickingStagingLocationId.HasValue)
                excluded.Add(row.ProductionPickingStagingLocationId.Value);
        }
        return excluded;
    }

    internal static async Task<HashSet<long>> FromTransferAsync(
        IUnitOfWork uow,CreateWarehouseTransferDraftRequest transfer,CancellationToken ct)
    {
        var excluded=FromTransfer(transfer);
        var warehouseIds=new[]{transfer.SourceWarehouseId,transfer.TargetWarehouseId}.Distinct().ToArray();
        var defaults=await uow.Repository<WarehouseEntity>().Query()
            .Where(x=>warehouseIds.Contains(x.Id))
            .Select(x=>new{x.DefaultProductionTransferLocationId,x.ProductionPickingStagingLocationId})
            .ToListAsync(ct);
        foreach(var row in defaults)
        {
            if(row.DefaultProductionTransferLocationId.HasValue)
                excluded.Add(row.DefaultProductionTransferLocationId.Value);
            if(row.ProductionPickingStagingLocationId.HasValue)
                excluded.Add(row.ProductionPickingStagingLocationId.Value);
        }
        return excluded;
    }

    private static void AddIfHasValue(ISet<long> excluded,long? locationId)
    {
        if(locationId.HasValue)excluded.Add(locationId.Value);
    }
}
