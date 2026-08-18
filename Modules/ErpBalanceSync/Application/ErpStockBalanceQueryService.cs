using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.ErpBalanceSync.Application;

public sealed class ErpStockBalanceQueryService(WmsDbContext dbContext) : IErpStockBalanceQueryService
{
    public Task<PagedResponse<ErpWarehouseStockBalanceRow>> GetBalancesAsync(
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        var search = request.LegacySearch?.Trim();
        var query =
            from balance in dbContext.ErpWarehouseStockBalances.AsNoTracking()
            join warehouse in dbContext.Warehouses.IgnoreQueryFilters().AsNoTracking()
                on balance.WarehouseId equals warehouse.Id into warehouseJoin
            from warehouse in warehouseJoin.DefaultIfEmpty()
            join stock in dbContext.Stocks.IgnoreQueryFilters().AsNoTracking()
                on balance.StockId equals stock.Id into stockJoin
            from stock in stockJoin.DefaultIfEmpty()
            where string.IsNullOrWhiteSpace(search)
               || balance.StockCode.Contains(search)
               || balance.WarehouseCode.ToString().Contains(search)
               || (warehouse != null && warehouse.WarehouseName.Contains(search))
               || (stock != null && stock.StockName.Contains(search))
               || balance.MappingStatus.Contains(search)
            select new ErpWarehouseStockBalanceRow(
                balance.Id,
                balance.WarehouseCode,
                warehouse != null ? warehouse.WarehouseName : null,
                balance.StockCode,
                stock != null ? stock.StockName : null,
                balance.UnitCode,
                balance.ErpQuantity,
                balance.WmsQuantityAtSync,
                balance.Difference,
                balance.MappingStatus,
                balance.IsMissingInErp,
                balance.FirstObservedAtUtc,
                balance.LastChangedAtUtc,
                balance.LastSyncRunId);

        return query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(ErpWarehouseStockBalanceRow.StockCode))
            .ToPagedResponseAsync(request, cancellationToken);
    }

    public Task<PagedResponse<ErpStockBalanceChangeRow>> GetChangesAsync(
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        var search = request.LegacySearch?.Trim();
        var query = dbContext.ErpStockBalanceChangeLogs.AsNoTracking()
            .Where(x => string.IsNullOrWhiteSpace(search)
                || x.StockCode.Contains(search)
                || x.WarehouseCode.ToString().Contains(search)
                || x.ChangeType.Contains(search)
                || x.ReasonCode.Contains(search))
            .Select(x => new ErpStockBalanceChangeRow(
                x.Id, x.SyncRunId, x.WarehouseCode, x.StockCode,
                x.PreviousErpQuantity, x.CurrentErpQuantity,
                x.PreviousWmsQuantity, x.CurrentWmsQuantity, x.Difference,
                x.ChangeType, x.ReasonCode, x.ObservedAtUtc));
        return query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(ErpStockBalanceChangeRow.ObservedAtUtc))
            .ToPagedResponseAsync(request, cancellationToken);
    }

    public Task<PagedResponse<ErpStockBalanceSyncRunRow>> GetRunsAsync(
        PagedRequest request,
        CancellationToken cancellationToken)
    {
        var search = request.LegacySearch?.Trim();
        var query = dbContext.ErpStockBalanceSyncRuns.AsNoTracking()
            .Where(x => string.IsNullOrWhiteSpace(search)
                || x.RunKey.ToString().Contains(search)
                || x.Mode.Contains(search)
                || x.TriggerSource.Contains(search)
                || x.Status.Contains(search)
                || (x.StockCode != null && x.StockCode.Contains(search))
                || (x.TriggerReference != null && x.TriggerReference.Contains(search)))
            .Select(x => new ErpStockBalanceSyncRunRow(
                x.Id, x.RunKey, x.Mode, x.TriggerSource, x.Status,
                x.WarehouseCode, x.StockCode, x.TriggerReference,
                x.StartedAtUtc, x.CompletedAtUtc, x.DurationMs,
                x.SourceCount, x.InsertedCount, x.UpdatedCount, x.UnchangedCount,
                x.MissingCount, x.DifferenceCount, x.UnmappedCount, x.ErrorMessage));
        return query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(ErpStockBalanceSyncRunRow.StartedAtUtc))
            .ToPagedResponseAsync(request, cancellationToken);
    }
}
