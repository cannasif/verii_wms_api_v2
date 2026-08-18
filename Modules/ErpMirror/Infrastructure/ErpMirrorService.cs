using Hangfire;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.ErpMirror.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.ErpMirror.Infrastructure;

public sealed class ErpMirrorService(IUnitOfWork unitOfWork, INetsisReadService netsis, ILogger<ErpMirrorService> logger) : IErpMirrorService
{
    private static readonly IReadOnlyDictionary<string, string> CustomerSearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nameof(CustomerMirrorDto.Id),
            ["branchCode"] = nameof(CustomerMirrorDto.BranchCode),
            ["businessUnitCode"] = nameof(CustomerMirrorDto.BusinessUnitCode),
            ["customerCode"] = nameof(CustomerMirrorDto.CustomerCode),
            ["customerName"] = nameof(CustomerMirrorDto.CustomerName),
            ["phone1"] = nameof(CustomerMirrorDto.Phone1),
            ["phone2"] = nameof(CustomerMirrorDto.Phone2),
            ["phone3"] = nameof(CustomerMirrorDto.Phone3),
            ["city"] = nameof(CustomerMirrorDto.City),
            ["district"] = nameof(CustomerMirrorDto.District),
            ["countryCode"] = nameof(CustomerMirrorDto.CountryCode),
            ["address"] = nameof(CustomerMirrorDto.Address),
            ["customerType"] = nameof(CustomerMirrorDto.CustomerType),
            ["taxOffice"] = nameof(CustomerMirrorDto.TaxOffice),
            ["email"] = nameof(CustomerMirrorDto.Email),
            ["website"] = nameof(CustomerMirrorDto.Website),
            ["createdBy"] = nameof(CustomerMirrorDto.CreatedBy),
            ["updatedBy"] = nameof(CustomerMirrorDto.UpdatedBy)
        };

    private static readonly string[] CustomerDefaultSearchColumns =
        ["branchCode", "customerCode", "customerName"];

    private IGenericRepository<WarehouseEntity> Warehouses => unitOfWork.Repository<WarehouseEntity>();
    private IGenericRepository<StockEntity> Stocks => unitOfWork.Repository<StockEntity>();
    private IGenericRepository<CustomerEntity> Customers => unitOfWork.Repository<CustomerEntity>();
    private IGenericRepository<YapCodeEntity> YapCodes => unitOfWork.Repository<YapCodeEntity>();

    [DisableConcurrentExecution(600)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 120, 300 })]
    public async Task<MirrorSyncResult> SyncWarehousesAsync(CancellationToken cancellationToken = default)
    {
        var source = (await netsis.GetWarehousesAsync(null, null, cancellationToken))
            .GroupBy(x => Key(x.SubeKodu, x.DepoKodu.ToString())).Select(x => x.First()).ToList();
        var existing = await Warehouses.Query(tracking: true, ignoreQueryFilters: true).ToListAsync(cancellationToken);
        var map = existing.ToDictionary(x => Key(x.BranchCode, x.WarehouseCode.ToString()), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow; var inserted = 0; var updated = 0;
        foreach (var row in source)
        {
            var branch = row.SubeKodu.ToString(); var key = Key(branch, row.DepoKodu.ToString());
            if (!map.TryGetValue(key, out var entity))
            {
                entity = new WarehouseEntity { BranchCode = branch, WarehouseCode = row.DepoKodu, WarehouseName = Clean(row.DepoIsmi, row.DepoKodu.ToString()), CreatedDate = now };
                await Warehouses.AddAsync(entity, cancellationToken); map[key] = entity; inserted++;
            }
            else { entity.WarehouseName = Clean(row.DepoIsmi, row.DepoKodu.ToString()); updated++; }
            Activate(entity, now); entity.LastSyncDate = now;
        }
        var deactivated = SoftDeleteMissing(existing, source.Select(x => Key(x.SubeKodu, x.DepoKodu.ToString())).ToHashSet(StringComparer.OrdinalIgnoreCase), x => Key(x.BranchCode, x.WarehouseCode.ToString()), now);
        await unitOfWork.SaveChangesAsync(cancellationToken); return Log(new("Warehouse", source.Count, inserted, updated, deactivated));
    }

    [DisableConcurrentExecution(600)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 120, 300 })]
    public async Task<MirrorSyncResult> SyncStocksAsync(CancellationToken cancellationToken = default)
    {
        var source = (await netsis.GetStocksAsync(null, null, cancellationToken))
            .Where(x => !string.IsNullOrWhiteSpace(x.StokKodu)).GroupBy(x => Key(x.SubeKodu, x.StokKodu)).Select(x => x.First()).ToList();
        var existing = await Stocks.Query(tracking: true, ignoreQueryFilters: true).ToListAsync(cancellationToken);
        var map = existing.ToDictionary(x => Key(x.BranchCode, x.ErpStockCode), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow; var inserted = 0; var updated = 0;
        foreach (var row in source)
        {
            var branch = row.SubeKodu.ToString(); var code = Normalize(row.StokKodu); var key = Key(branch, code);
            if (!map.TryGetValue(key, out var entity))
            {
                entity = new StockEntity { BranchCode = branch, ErpStockCode = code, CreatedDate = now };
                await Stocks.AddAsync(entity, cancellationToken); map[key] = entity; inserted++;
            }
            else updated++;
            entity.BusinessUnitCode = row.IsletmeKodu; entity.StockName = Clean(row.StokAdi, code);
            entity.BaseUnitCode = CleanUnit(row.OlcuBr1, entity.BaseUnitCode);
            entity.ManufacturerCode = Trim(row.UreticiKodu); entity.GroupCode = Trim(row.GrupKodu);
            entity.Code1 = Trim(row.Kod1); entity.Code2 = Trim(row.Kod2); entity.Code3 = Trim(row.Kod3); entity.Code4 = Trim(row.Kod4); entity.Code5 = Trim(row.Kod5);
            Activate(entity, now); entity.LastSyncDate = now;
        }
        var deactivated = SoftDeleteMissing(existing, source.Select(x => Key(x.SubeKodu, x.StokKodu)).ToHashSet(StringComparer.OrdinalIgnoreCase), x => Key(x.BranchCode, x.ErpStockCode), now);
        await unitOfWork.SaveChangesAsync(cancellationToken); return Log(new("Stock", source.Count, inserted, updated, deactivated));
    }

    [DisableConcurrentExecution(600)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 120, 300 })]
    public async Task<MirrorSyncResult> SyncCustomersAsync(CancellationToken cancellationToken = default)
    {
        var source = (await netsis.GetCustomersAsync(null, null, cancellationToken))
            .Where(x => !string.IsNullOrWhiteSpace(x.CariKod)).GroupBy(x => Key(x.SubeKodu, x.CariKod)).Select(x => x.First()).ToList();
        var existing = await Customers.Query(tracking: true, ignoreQueryFilters: true).ToListAsync(cancellationToken);
        var map = existing.ToDictionary(x => Key(x.BranchCode, x.CustomerCode), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow; var inserted = 0; var updated = 0;
        foreach (var row in source)
        {
            var branch = row.SubeKodu.ToString(); var code = Normalize(row.CariKod); var key = Key(branch, code);
            if (!map.TryGetValue(key, out var entity))
            {
                entity = new CustomerEntity { BranchCode = branch, CustomerCode = code, CreatedDate = now };
                await Customers.AddAsync(entity, cancellationToken); map[key] = entity; inserted++;
            }
            else updated++;
            entity.BusinessUnitCode = row.IsletmeKodu;
            entity.CustomerName = Clean(row.CariIsim, code);
            entity.Phone1 = Trim(row.CariTel);
            entity.Phone2 = Trim(row.CariTel2);
            entity.Phone3 = Trim(row.CariTel3);
            entity.City = Trim(row.CariIl);
            entity.District = Trim(row.CariIlce);
            entity.CountryCode = Trim(row.UlkeKodu);
            entity.Address = Trim(row.CariAdres);
            entity.CustomerType = Trim(row.CariTip);
            entity.TaxOffice = Trim(row.VergiDairesi);
            entity.Email = Trim(row.Email);
            entity.Website = Trim(row.Web);
            Activate(entity, now); entity.LastSyncDate = now;
        }
        var deactivated = SoftDeleteMissing(existing, source.Select(x => Key(x.SubeKodu, x.CariKod)).ToHashSet(StringComparer.OrdinalIgnoreCase), x => Key(x.BranchCode, x.CustomerCode), now);
        await unitOfWork.SaveChangesAsync(cancellationToken); return Log(new("Customer", source.Count, inserted, updated, deactivated));
    }

    [DisableConcurrentExecution(600)]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 120, 300 })]
    public async Task<MirrorSyncResult> SyncConfigurationCodesAsync(CancellationToken cancellationToken = default)
    {
        var source = (await netsis.GetConfigurationCodesAsync(null, null, cancellationToken))
            .Where(x => !string.IsNullOrWhiteSpace(x.ConfigurationCode))
            .GroupBy(x => Key(x.BranchCode ?? 0, x.ConfigurationCode))
            .Select(x => x.First())
            .ToList();
        var existing = await YapCodes.Query(tracking: true, ignoreQueryFilters: true).ToListAsync(cancellationToken);
        var stocks = await Stocks.Query().ToListAsync(cancellationToken);
        var stockMap = stocks.ToDictionary(x => Key(x.BranchCode, x.ErpStockCode), StringComparer.OrdinalIgnoreCase);
        var map = existing.ToDictionary(x => Key(x.BranchCode, x.ConfigurationCode), StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow; var inserted = 0; var updated = 0;
        foreach (var row in source)
        {
            var branch = (row.BranchCode ?? 0).ToString(); var code = Normalize(row.ConfigurationCode); var key = Key(branch, code);
            if (!map.TryGetValue(key, out var entity))
            {
                entity = new YapCodeEntity { BranchCode = branch, ConfigurationCode = code, CreatedDate = now };
                await YapCodes.AddAsync(entity, cancellationToken); map[key] = entity; inserted++;
            }
            else updated++;
            entity.Description = Clean(row.Description, code); entity.ConfigurableStockCode = Trim(row.ConfigurableStockCode);
            entity.StockId = entity.ConfigurableStockCode is not null && stockMap.TryGetValue(Key(branch, entity.ConfigurableStockCode), out var stock) ? stock.Id : null;
            Activate(entity, now); entity.LastSyncDate = now;
        }
        var deactivated = SoftDeleteMissing(existing, source.Select(x => Key(x.BranchCode ?? 0, x.ConfigurationCode)).ToHashSet(StringComparer.OrdinalIgnoreCase), x => Key(x.BranchCode, x.ConfigurationCode), now);
        await unitOfWork.SaveChangesAsync(cancellationToken); return Log(new("ConfigurationCode", source.Count, inserted, updated, deactivated));
    }

    public async Task<IReadOnlyList<MirrorSyncResult>> SyncAllAsync(CancellationToken cancellationToken = default) => new[]
    {
        await SyncWarehousesAsync(cancellationToken), await SyncStocksAsync(cancellationToken),
        await SyncCustomersAsync(cancellationToken), await SyncConfigurationCodesAsync(cancellationToken)
    };

    public async Task<PagedResponse<WarehouseMirrorDto>> GetWarehousesPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var search = request.Search?.Trim();
        var query = Warehouses.Query()
            .Where(x => string.IsNullOrWhiteSpace(search) || x.BranchCode.Contains(search) || x.WarehouseName.Contains(search) || x.WarehouseCode.ToString().Contains(search))
            .Select(x => new WarehouseMirrorDto(x.Id, x.BranchCode, x.WarehouseCode, x.WarehouseName,
                x.DefaultGoodsReceiptLocationId, x.LastSyncDate, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate))
            .ApplyAdvancedFilters(request).ApplySort(request, nameof(WarehouseMirrorDto.WarehouseCode));
        var page = await PageAsync(query, request, ct);
        var flags = await ProductionTransferWarehouseRacklessSupport.GetRacklessFlagsAsync(
            unitOfWork, page.Items.Select(x => x.Id).ToArray(), ct);
        return new PagedResponse<WarehouseMirrorDto>
        {
            Items = page.Items
                .Select(x => x with { IsRackless = flags.GetValueOrDefault(x.Id) })
                .ToArray(),
            TotalCount = page.TotalCount,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
        };
    }
    public Task<PagedResponse<StockMirrorDto>> GetStocksPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var search = request.Search?.Trim();
        var query = Stocks.Query()
            .Where(x => string.IsNullOrWhiteSpace(search) || x.BranchCode.Contains(search) || x.ErpStockCode.Contains(search) || x.StockName.Contains(search) || x.BaseUnitCode.Contains(search) || (x.ManufacturerCode != null && x.ManufacturerCode.Contains(search)) || (x.GroupCode != null && x.GroupCode.Contains(search)))
            .Select(x => new StockMirrorDto(x.Id, x.BranchCode, x.BusinessUnitCode, x.ErpStockCode, x.StockName, x.BaseUnitCode, x.ManufacturerCode, x.GroupCode, x.Code1, x.Code2, x.Code3, x.Code4, x.Code5, x.LastSyncDate, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate))
            .ApplyAdvancedFilters(request).ApplySort(request, nameof(StockMirrorDto.ErpStockCode));
        return PageAsync(query, request, ct);
    }
    public Task<PagedResponse<CustomerMirrorDto>> GetCustomersPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var query = Customers.Query()
            .Select(x => new CustomerMirrorDto(
                x.Id,
                x.BranchCode,
                x.BusinessUnitCode,
                x.CustomerCode,
                x.CustomerName,
                x.Phone1,
                x.Phone2,
                x.Phone3,
                x.City,
                x.District,
                x.CountryCode,
                x.Address,
                x.CustomerType,
                x.TaxOffice,
                x.Email,
                x.Website,
                x.LastSyncDate,
                x.CreatedBy,
                x.CreatedDate,
                x.UpdatedBy,
                x.UpdatedDate))
            .ApplySearch(
                request,
                CustomerSearchColumns,
                CustomerDefaultSearchColumns)
            .ApplyAdvancedFilters(request).ApplySort(request, nameof(CustomerMirrorDto.CustomerCode));
        return PageAsync(query, request, ct);
    }
    public Task<PagedResponse<ConfigurationCodeMirrorDto>> GetConfigurationCodesPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var search = request.Search?.Trim();
        var query = YapCodes.Query()
            .Where(x => string.IsNullOrWhiteSpace(search) || x.BranchCode.Contains(search) || x.ConfigurationCode.Contains(search) || x.Description.Contains(search) || (x.ConfigurableStockCode != null && x.ConfigurableStockCode.Contains(search)))
            .Select(x => new ConfigurationCodeMirrorDto(x.Id, x.BranchCode, x.ConfigurationCode, x.Description, x.ConfigurableStockCode, x.StockId, x.LastSyncDate, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate))
            .ApplyAdvancedFilters(request).ApplySort(request, nameof(ConfigurationCodeMirrorDto.ConfigurationCode));
        return PageAsync(query, request, ct);
    }

    private static async Task<PagedResponse<T>> PageAsync<T>(IQueryable<T> query, PagedRequest request, CancellationToken ct)
    {
        return await query.ToPagedResponseAsync(request, ct);
    }
    private static string Key(object branch, string code) => $"{branch}|{Normalize(code)}";
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static string CleanUnit(string? sourceUnit, string? currentUnit)
    {
        var unit = Trim(sourceUnit) ?? Trim(currentUnit) ?? "ADET";
        return unit.ToUpperInvariant();
    }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Clean(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static void Activate(verii_wms_api_v2.Shared.Domain.BaseEntity entity, DateTime now) { entity.IsDeleted = false; entity.DeletedDate = null; entity.DeletedBy = null; entity.UpdatedDate = now; }
    private static int SoftDeleteMissing<T>(IEnumerable<T> existing, HashSet<string> sourceKeys, Func<T, string> key, DateTime now) where T : verii_wms_api_v2.Shared.Domain.BaseEntity
    {
        if (sourceKeys.Count == 0) return 0; var count = 0;
        foreach (var entity in existing.Where(x => !x.IsDeleted && !sourceKeys.Contains(key(x)))) { entity.IsDeleted = true; entity.DeletedDate = now; entity.UpdatedDate = now; count++; }
        return count;
    }
    private MirrorSyncResult Log(MirrorSyncResult result) { logger.LogInformation("ERP mirror sync completed: {@Result}", result); return result; }
}
