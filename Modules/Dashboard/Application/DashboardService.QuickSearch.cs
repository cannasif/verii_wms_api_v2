using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Dashboard.Application;

public sealed partial class DashboardService
{
    private const int QuickSearchLimit = 5;
    private const int QuickSearchMinLength = 1;
    private const int QuickSearchTextMinLength = 2;
    private const int QuickSearchMaxLength = 64;
    private static readonly Dictionary<string, string> StockSearchColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["erpStockCode"] = nameof(StockEntity.ErpStockCode),
        ["stockName"] = nameof(StockEntity.StockName),
        ["manufacturerCode"] = nameof(StockEntity.ManufacturerCode),
    };

    private static readonly string[] StockSearchFields = ["erpStockCode", "stockName", "manufacturerCode"];

    public async Task<DashboardQuickSearchResult> GetQuickSearchAsync(
        long currentUserId,
        string? branchCode,
        string? query,
        string? scopes = null,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId <= 0)
            throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");

        var term = NormalizeQuickSearchQuery(query);
        if (term is null)
            return DashboardQuickSearchResult.Empty;

        var wanted = ParseQuickSearchScopes(scopes);
        var allowTextSources = term.Length >= QuickSearchTextMinLength;
        var branch = NormalizeBranchCode(branchCode);
        var warehouseScope = await ResolveWarehouseScopeAsync(currentUserId, cancellationToken);

        var stocks = allowTextSources && wanted.Stock
            ? await SearchStocksAsync(term, cancellationToken)
            : [];
        var warehouses = wanted.Warehouse
            ? await SafeInventorySearchAsync(() => SearchWarehousesAsync(term, warehouseScope, cancellationToken))
            : [];
        var locations = allowTextSources && wanted.Location
            ? await SafeInventorySearchAsync(() => SearchLocationsAsync(term, warehouseScope, cancellationToken))
            : [];
        var serials = allowTextSources && wanted.Serial
            ? await SafeInventorySearchAsync(() => SearchSerialsAsync(term, warehouseScope, cancellationToken))
            : [];
        var lots = allowTextSources && wanted.Lot
            ? await SafeInventorySearchAsync(() => SearchLotsAsync(term, warehouseScope, cancellationToken))
            : [];
        var goodsReceipts = allowTextSources && wanted.Document
            ? await SearchGoodsReceiptsAsync(branch, warehouseScope, term, cancellationToken)
            : [];
        var shipments = allowTextSources && wanted.Shipment
            ? await SearchShipmentsAsync(branch, warehouseScope, term, cancellationToken)
            : [];
        var transfers = allowTextSources && wanted.Document
            ? await SearchTransfersAsync(branch, warehouseScope, term, cancellationToken)
            : [];

        return new DashboardQuickSearchResult(
        [
            .. stocks,
            .. warehouses,
            .. locations,
            .. serials,
            .. lots,
            .. goodsReceipts,
            .. shipments,
            .. transfers,
        ]);
    }

    private async Task<List<DashboardQuickSearchHit>> SearchStocksAsync(
        string term,
        CancellationToken cancellationToken)
    {
        var request = new PagedRequest
        {
            Search = term,
            SearchFields = StockSearchFields,
            PageSize = QuickSearchLimit,
        };

        IQueryable<StockEntity> query = dbContext.Stocks.AsNoTracking().Where(x => !x.IsDeleted);
        if (UsesSqlServer)
        {
            query = query.ApplySearch(
                request,
                StockSearchColumns,
                StockSearchFields);
        }
        else
        {
            var matched = (await query
                    .Select(x => new { x.Id, x.ErpStockCode, x.StockName, x.ManufacturerCode })
                    .ToListAsync(cancellationToken))
                .Where(x =>
                    AsciiTurkishSearch.Contains(x.ErpStockCode, term)
                    || AsciiTurkishSearch.Contains(x.StockName, term)
                    || AsciiTurkishSearch.Contains(x.ManufacturerCode, term))
                .OrderBy(x => x.ErpStockCode)
                .Take(QuickSearchLimit)
                .ToList();

            return matched
                .Select(x => new DashboardQuickSearchHit(
                    "stock",
                    x.Id.ToString(),
                    x.ErpStockCode,
                    DisplaySubtitle(x.StockName, null),
                    $"/erp/stocks?code={Uri.EscapeDataString(x.ErpStockCode)}"))
                .ToList();
        }

        var rows = await query
            .OrderBy(x => x.ErpStockCode)
            .Take(QuickSearchLimit)
            .Select(x => new { x.Id, x.ErpStockCode, x.StockName })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new DashboardQuickSearchHit(
                "stock",
                x.Id.ToString(),
                x.ErpStockCode,
                DisplaySubtitle(x.StockName, null),
                $"/erp/stocks?code={Uri.EscapeDataString(x.ErpStockCode)}"))
            .ToList();
    }

    private async Task<List<DashboardQuickSearchHit>> SearchWarehousesAsync(
        string term,
        WarehouseScope warehouseScope,
        CancellationToken cancellationToken)
    {
        IQueryable<WarehouseEntity> query = dbContext.Warehouses.AsNoTracking().Where(x => !x.IsDeleted);
        if (warehouseScope.Restricted)
            query = query.Where(x => warehouseScope.WarehouseIds.Contains(x.Id));

        var parsedCode = int.TryParse(term, out var warehouseCode) ? warehouseCode : (int?)null;
        if (UsesSqlServer)
        {
            var pattern = AsciiTurkishSearch.BuildContainsPattern(term);
            query = parsedCode.HasValue
                ? query.Where(x => EF.Functions.Like(x.WarehouseName, pattern, AsciiTurkishSearch.LikeEscapeCharacter)
                    || x.WarehouseCode == parsedCode.Value)
                : query.Where(x => EF.Functions.Like(x.WarehouseName, pattern, AsciiTurkishSearch.LikeEscapeCharacter));
        }
        else
        {
            var matched = (await query
                    .Select(x => new { x.Id, x.WarehouseCode, x.WarehouseName })
                    .ToListAsync(cancellationToken))
                .Where(x => (parsedCode.HasValue && x.WarehouseCode == parsedCode.Value)
                    || AsciiTurkishSearch.Contains(x.WarehouseName, term))
                .OrderBy(x => x.WarehouseName)
                .Take(QuickSearchLimit)
                .ToList();

            return matched
                .Select(x => WarehouseHit(x.Id, x.WarehouseCode, x.WarehouseName))
                .ToList();
        }

        var rows = await query
            .OrderBy(x => x.WarehouseName)
            .Take(QuickSearchLimit)
            .Select(x => new { x.Id, x.WarehouseCode, x.WarehouseName })
            .ToListAsync(cancellationToken);

        return rows.Select(x => WarehouseHit(x.Id, x.WarehouseCode, x.WarehouseName)).ToList();
    }

    private async Task<List<DashboardQuickSearchHit>> SearchLocationsAsync(
        string term,
        WarehouseScope warehouseScope,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Locations.AsNoTracking().Where(x => !x.IsDeleted);
        if (warehouseScope.Restricted)
            query = query.Where(x => warehouseScope.WarehouseIds.Contains(x.WarehouseId));

        if (UsesSqlServer)
        {
            var pattern = AsciiTurkishSearch.BuildContainsPattern(term);
            query = query.Where(x =>
                EF.Functions.Like(x.Code, pattern, AsciiTurkishSearch.LikeEscapeCharacter)
                || EF.Functions.Like(x.Name, pattern, AsciiTurkishSearch.LikeEscapeCharacter)
                || (x.Barcode != null && EF.Functions.Like(x.Barcode, pattern, AsciiTurkishSearch.LikeEscapeCharacter)));
        }
        else
        {
            var matched = (await query
                    .Select(x => new { x.Id, x.Code, x.Name, x.Barcode, x.WarehouseId })
                    .ToListAsync(cancellationToken))
                .Where(x =>
                    AsciiTurkishSearch.Contains(x.Code, term)
                    || AsciiTurkishSearch.Contains(x.Name, term)
                    || AsciiTurkishSearch.Contains(x.Barcode, term))
                .OrderBy(x => x.Code)
                .Take(QuickSearchLimit)
                .ToList();

            var warehouseNames = await WarehouseNamesAsync(matched.Select(x => x.WarehouseId), cancellationToken);
            return matched
                .Select(x => LocationHit(x.Id, x.Code, x.Name, warehouseNames.GetValueOrDefault(x.WarehouseId)))
                .ToList();
        }

        var rows = await query
            .OrderBy(x => x.Code)
            .Take(QuickSearchLimit)
            .Select(x => new { x.Id, x.Code, x.Name, x.WarehouseId })
            .ToListAsync(cancellationToken);
        var names = await WarehouseNamesAsync(rows.Select(x => x.WarehouseId), cancellationToken);
        return rows
            .Select(x => LocationHit(x.Id, x.Code, x.Name, names.GetValueOrDefault(x.WarehouseId)))
            .ToList();
    }

    private async Task<List<DashboardQuickSearchHit>> SearchSerialsAsync(
        string term,
        WarehouseScope warehouseScope,
        CancellationToken cancellationToken)
    {
        var query = dbContext.LocationStockBalances.AsNoTracking()
            .Where(x => x.SerialNo != "");
        if (warehouseScope.Restricted)
            query = query.Where(x => warehouseScope.WarehouseIds.Contains(x.WarehouseId));

        var exact = await query
            .Where(x => x.SerialNo == term)
            .OrderByDescending(x => x.AvailableQuantity)
            .Take(QuickSearchLimit)
            .Select(x => new { x.Id, x.SerialNo, x.StockId, x.WarehouseId, x.LocationId })
            .ToListAsync(cancellationToken);

        var rows = exact;
        if (rows.Count == 0)
        {
            if (UsesSqlServer)
            {
                var pattern = AsciiTurkishSearch.BuildContainsPattern(term);
                rows = await query
                    .Where(x => EF.Functions.Like(x.SerialNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter))
                    .OrderByDescending(x => x.AvailableQuantity)
                    .Take(QuickSearchLimit)
                    .Select(x => new { x.Id, x.SerialNo, x.StockId, x.WarehouseId, x.LocationId })
                    .ToListAsync(cancellationToken);
            }
            else
            {
                rows = (await query
                        .Select(x => new { x.Id, x.SerialNo, x.StockId, x.WarehouseId, x.LocationId, x.AvailableQuantity })
                        .ToListAsync(cancellationToken))
                    .Where(x => AsciiTurkishSearch.Contains(x.SerialNo, term))
                    .OrderByDescending(x => x.AvailableQuantity)
                    .Take(QuickSearchLimit)
                    .Select(x => new { x.Id, x.SerialNo, x.StockId, x.WarehouseId, x.LocationId })
                    .ToList();
            }
        }

        if (rows.Count == 0) return [];

        var stockIds = rows.Select(x => x.StockId).Distinct().ToArray();
        var locationIds = rows.Select(x => x.LocationId).Distinct().ToArray();
        var stocks = await dbContext.Stocks.AsNoTracking()
            .Where(x => stockIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ErpStockCode })
            .ToDictionaryAsync(x => x.Id, x => x.ErpStockCode, cancellationToken);
        var locations = await dbContext.Locations.AsNoTracking()
            .Where(x => locationIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code })
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        return rows
            .Select(x => new DashboardQuickSearchHit(
                "serial",
                x.Id.ToString(),
                x.SerialNo,
                DisplaySubtitle(
                    stocks.GetValueOrDefault(x.StockId),
                    locations.GetValueOrDefault(x.LocationId)),
                $"/warehouse/stock-balances/serials?open={x.Id}"))
            .ToList();
    }

    private async Task<List<DashboardQuickSearchHit>> SearchLotsAsync(
        string term,
        WarehouseScope warehouseScope,
        CancellationToken cancellationToken)
    {
        var query = dbContext.LocationStockBalances.AsNoTracking()
            .Where(x => x.LotNo != "");
        if (warehouseScope.Restricted)
            query = query.Where(x => warehouseScope.WarehouseIds.Contains(x.WarehouseId));

        List<string> lotNos;
        if (UsesSqlServer)
        {
            var pattern = AsciiTurkishSearch.BuildContainsPattern(term);
            lotNos = await query
                .Where(x => x.LotNo == term
                    || EF.Functions.Like(x.LotNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter))
                .Select(x => x.LotNo)
                .Distinct()
                .OrderBy(x => x)
                .Take(QuickSearchLimit)
                .ToListAsync(cancellationToken);
        }
        else
        {
            lotNos = (await query.Select(x => x.LotNo).Distinct().ToListAsync(cancellationToken))
                .Where(lot => lot.Equals(term, StringComparison.OrdinalIgnoreCase)
                    || AsciiTurkishSearch.Contains(lot, term))
                .OrderBy(lot => lot)
                .Take(QuickSearchLimit)
                .ToList();
        }

        return lotNos
            .Select(lot => new DashboardQuickSearchHit(
                "lot",
                lot,
                lot,
                "Lot",
                $"/warehouse/stock-balances/locations?lot={Uri.EscapeDataString(lot)}"))
            .ToList();
    }

    private static async Task<List<DashboardQuickSearchHit>> SafeInventorySearchAsync(
        Func<Task<List<DashboardQuickSearchHit>>> search)
    {
        try
        {
            return await search();
        }
        catch
        {
            return [];
        }
    }

    private async Task<Dictionary<long, string>> WarehouseNamesAsync(
        IEnumerable<long> warehouseIds,
        CancellationToken cancellationToken)
    {
        var ids = warehouseIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await dbContext.Warehouses.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.WarehouseName, cancellationToken);
    }

    private static DashboardQuickSearchHit WarehouseHit(long id, int code, string name) =>
        new(
            "warehouse",
            id.ToString(),
            string.IsNullOrWhiteSpace(name) ? code.ToString() : name.Trim(),
            DisplaySubtitle(null, code.ToString()),
            $"/warehouse/stock-balances?warehouse={id}");

    private static DashboardQuickSearchHit LocationHit(long id, string code, string name, string? warehouseName) =>
        new(
            "location",
            id.ToString(),
            string.IsNullOrWhiteSpace(code) ? $"#{id}" : code.Trim(),
            DisplaySubtitle(name, warehouseName),
            $"/warehouse/stock-balances/locations?open={id}");

    private async Task<List<DashboardQuickSearchHit>> SearchGoodsReceiptsAsync(
        string branch,
        WarehouseScope warehouseScope,
        string term,
        CancellationToken cancellationToken)
    {
        var query = dbContext.GoodsReceiptHeaders
            .AsNoTracking()
            .Where(x => x.BranchCode == branch)
            .Where(x => !warehouseScope.Restricted || warehouseScope.WarehouseIds.Contains(x.TargetWarehouseId));

        if (UsesSqlServer)
        {
            var pattern = AsciiTurkishSearch.BuildContainsPattern(term);
            query = query.Where(x =>
                EF.Functions.Like(x.DocumentNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter)
                || (x.WaybillNo != null && EF.Functions.Like(x.WaybillNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter))
                || (x.ElectronicWaybillNo != null && EF.Functions.Like(x.ElectronicWaybillNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter))
                || (x.ExternalReferenceNo != null && EF.Functions.Like(x.ExternalReferenceNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter)));
        }
        else
        {
            query = query.Where(x =>
                x.DocumentNo.Contains(term)
                || (x.WaybillNo != null && x.WaybillNo.Contains(term))
                || (x.ElectronicWaybillNo != null && x.ElectronicWaybillNo.Contains(term))
                || (x.ExternalReferenceNo != null && x.ExternalReferenceNo.Contains(term)));
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedDate)
            .Take(QuickSearchLimit)
            .Select(x => new
            {
                x.Id,
                x.DocumentNo,
                x.SupplierNameSnapshot,
                x.SupplierCodeSnapshot,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new DashboardQuickSearchHit(
                "goods-receipt",
                x.Id.ToString(),
                DisplayTitle(x.DocumentNo, x.Id),
                DisplaySubtitle(x.SupplierNameSnapshot, x.SupplierCodeSnapshot),
                $"/warehouse/goods-receipts/list?open={x.Id}"))
            .ToList();
    }

    private async Task<List<DashboardQuickSearchHit>> SearchShipmentsAsync(
        string branch,
        WarehouseScope warehouseScope,
        string term,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ShipmentHeaders
            .AsNoTracking()
            .Where(x => x.BranchCode == branch)
            .Where(x => !warehouseScope.Restricted || warehouseScope.WarehouseIds.Contains(x.SourceWarehouseId));

        if (UsesSqlServer)
        {
            var pattern = AsciiTurkishSearch.BuildContainsPattern(term);
            query = query.Where(x =>
                EF.Functions.Like(x.DocumentNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter)
                || (x.WaybillNo != null && EF.Functions.Like(x.WaybillNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter))
                || (x.ExternalReferenceNo != null && EF.Functions.Like(x.ExternalReferenceNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter)));
        }
        else
        {
            query = query.Where(x =>
                x.DocumentNo.Contains(term)
                || (x.WaybillNo != null && x.WaybillNo.Contains(term))
                || (x.ExternalReferenceNo != null && x.ExternalReferenceNo.Contains(term)));
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedDate)
            .Take(QuickSearchLimit)
            .Select(x => new
            {
                x.Id,
                x.DocumentNo,
                x.CustomerNameSnapshot,
                x.CustomerCodeSnapshot,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new DashboardQuickSearchHit(
                "shipment",
                x.Id.ToString(),
                DisplayTitle(x.DocumentNo, x.Id),
                DisplaySubtitle(x.CustomerNameSnapshot, x.CustomerCodeSnapshot),
                $"/warehouse/shipments/{x.Id}/operations"))
            .ToList();
    }

    private async Task<List<DashboardQuickSearchHit>> SearchTransfersAsync(
        string branch,
        WarehouseScope warehouseScope,
        string term,
        CancellationToken cancellationToken)
    {
        var query = dbContext.WarehouseTransferHeaders
            .AsNoTracking()
            .Where(x => x.BranchCode == branch)
            .Where(x => !warehouseScope.Restricted
                || warehouseScope.WarehouseIds.Contains(x.SourceWarehouseId)
                || warehouseScope.WarehouseIds.Contains(x.TargetWarehouseId));

        if (UsesSqlServer)
        {
            var pattern = AsciiTurkishSearch.BuildContainsPattern(term);
            query = query.Where(x =>
                EF.Functions.Like(x.DocumentNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter)
                || (x.ExternalReferenceNo != null && EF.Functions.Like(x.ExternalReferenceNo, pattern, AsciiTurkishSearch.LikeEscapeCharacter)));
        }
        else
        {
            query = query.Where(x =>
                x.DocumentNo.Contains(term)
                || (x.ExternalReferenceNo != null && x.ExternalReferenceNo.Contains(term)));
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedDate)
            .Take(QuickSearchLimit)
            .Select(x => new
            {
                x.Id,
                x.DocumentNo,
                x.BusinessContext,
                x.ExternalReferenceNo,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new DashboardQuickSearchHit(
                "transfer",
                x.Id.ToString(),
                DisplayTitle(x.DocumentNo, x.Id),
                DisplaySubtitle(x.ExternalReferenceNo, x.BusinessContext.ToString()),
                TransferHref(x.BusinessContext, x.Id)))
            .ToList();
    }

    private bool UsesSqlServer =>
        dbContext.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    internal static string? NormalizeQuickSearchQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var trimmed = query.Trim();
        if (trimmed.Length < QuickSearchMinLength || trimmed.Length > QuickSearchMaxLength)
            return null;
        return trimmed;
    }

    private static QuickSearchScopeSet ParseQuickSearchScopes(string? scopes)
    {
        if (string.IsNullOrWhiteSpace(scopes)) return QuickSearchScopeSet.All;
        var wanted = QuickSearchScopeSet.None;
        foreach (var raw in scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Equals("stock", StringComparison.OrdinalIgnoreCase)) wanted = wanted with { Stock = true };
            else if (raw.Equals("warehouse", StringComparison.OrdinalIgnoreCase)) wanted = wanted with { Warehouse = true };
            else if (raw.Equals("location", StringComparison.OrdinalIgnoreCase)) wanted = wanted with { Location = true };
            else if (raw.Equals("serial", StringComparison.OrdinalIgnoreCase)) wanted = wanted with { Serial = true };
            else if (raw.Equals("lot", StringComparison.OrdinalIgnoreCase)) wanted = wanted with { Lot = true };
            else if (raw.Equals("document", StringComparison.OrdinalIgnoreCase)) wanted = wanted with { Document = true };
            else if (raw.Equals("shipment", StringComparison.OrdinalIgnoreCase)) wanted = wanted with { Shipment = true };
        }

        return wanted.IsEmpty ? QuickSearchScopeSet.All : wanted;
    }

    private readonly record struct QuickSearchScopeSet(
        bool Stock,
        bool Warehouse,
        bool Location,
        bool Serial,
        bool Lot,
        bool Document,
        bool Shipment)
    {
        public static readonly QuickSearchScopeSet All = new(true, true, true, true, true, true, true);
        public static readonly QuickSearchScopeSet None = new(false, false, false, false, false, false, false);
        public bool IsEmpty => !Stock && !Warehouse && !Location && !Serial && !Lot && !Document && !Shipment;
    }

    private static string TransferHref(WarehouseTransferBusinessContext context, long id) =>
        context switch
        {
            WarehouseTransferBusinessContext.ProductionMaterialSupply
                or WarehouseTransferBusinessContext.ProductionWipMove
                or WarehouseTransferBusinessContext.ProductionOutputMove
                => $"/warehouse/production-transfers/{id}/operations",
            WarehouseTransferBusinessContext.SubcontractingIssue
                or WarehouseTransferBusinessContext.SubcontractingReceipt
                or WarehouseTransferBusinessContext.SubcontractorToSubcontractor
                => $"/warehouse/subcontracting-transfers/{id}/operations",
            _ => $"/warehouse/transfers/{id}/operations",
        };
}
