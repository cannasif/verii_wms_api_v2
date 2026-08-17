using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed class SupplierStockMappingService(
    IUnitOfWork unitOfWork,
    IAuditLogWriter audit) : ISupplierStockMappingService
{
    private static readonly IReadOnlyDictionary<string, string> SearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["supplierCode"] = nameof(SupplierStockMappingRow.SupplierSearchText),
            ["supplierName"] = nameof(SupplierStockMappingRow.SupplierName),
            ["supplierStockCode"] = nameof(SupplierStockMappingRow.SupplierStockCode),
            ["supplierStockName"] = nameof(SupplierStockMappingRow.SupplierStockName),
            ["systemStockCode"] = nameof(SupplierStockMappingRow.SystemStockSearchText),
            ["systemStockName"] = nameof(SupplierStockMappingRow.SystemStockName),
            ["supplierUnitCode"] = nameof(SupplierStockMappingRow.SupplierUnitCode),
            ["systemUnitCode"] = nameof(SupplierStockMappingRow.SystemUnitCode),
            ["notes"] = nameof(SupplierStockMappingRow.Notes)
        };
    private static readonly string[] DefaultSearchColumns =
    [
        "supplierCode", "supplierName", "supplierStockCode",
        "supplierStockName", "systemStockCode", "systemStockName"
    ];

    private IGenericRepository<SupplierStockMapping> Mappings =>
        unitOfWork.Repository<SupplierStockMapping>();

    public Task<PagedResponse<SupplierStockMappingRow>> GetPagedAsync(
        string branchCode, PagedRequest request, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        return BuildRows(branch)
            .ApplySearch(request, SearchColumns, DefaultSearchColumns)
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(SupplierStockMappingRow.SupplierCode))
            .ToPagedResponseAsync(request, ct);
    }

    public async Task<SupplierStockMappingRow> GetAsync(
        long id, string branchCode, CancellationToken ct = default) =>
        await BuildRows(NormalizeBranch(branchCode))
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw AppException.NotFound("Tedarikçi stok eşlemesi bulunamadı.");

    public async Task<SupplierStockMappingRow> CreateAsync(
        SaveSupplierStockMappingRequest request, CancellationToken ct = default)
    {
        var value = await ValidateAsync(request, null, ct);
        var entity = new SupplierStockMapping
        {
            BranchCode = value.BranchCode,
            SupplierId = request.SupplierId,
            SupplierStockCode = value.SupplierStockCode,
            NormalizedSupplierStockCode = value.NormalizedSupplierStockCode,
            SupplierStockName = value.SupplierStockName,
            SupplierUnitCode = value.SupplierUnitCode,
            StockId = request.StockId,
            ConversionFactor = request.ConversionFactor,
            IsActive = request.IsActive,
            Notes = value.Notes
        };
        await Mappings.AddAsync(entity, ct);
        await SaveAsync(ct);
        await WriteAuditAsync("supplier-stock-mapping.create", entity, null, ct);
        return await GetAsync(entity.Id, value.BranchCode, ct);
    }

    public async Task<SupplierStockMappingRow> UpdateAsync(
        long id, SaveSupplierStockMappingRequest request, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(request.BranchCode);
        var entity = await Mappings.Query(tracking: true)
            .FirstOrDefaultAsync(x => x.Id == id && x.BranchCode == branch, ct)
            ?? throw AppException.NotFound("Tedarikçi stok eşlemesi bulunamadı.");
        EnsureRowVersion(entity, request.RowVersion);
        var value = await ValidateAsync(request, id, ct);
        var oldValues = Snapshot(entity);
        entity.SupplierId = request.SupplierId;
        entity.SupplierStockCode = value.SupplierStockCode;
        entity.NormalizedSupplierStockCode = value.NormalizedSupplierStockCode;
        entity.SupplierStockName = value.SupplierStockName;
        entity.SupplierUnitCode = value.SupplierUnitCode;
        entity.StockId = request.StockId;
        entity.ConversionFactor = request.ConversionFactor;
        entity.IsActive = request.IsActive;
        entity.Notes = value.Notes;
        Mappings.Update(entity);
        await SaveAsync(ct);
        await WriteAuditAsync("supplier-stock-mapping.update", entity, oldValues, ct);
        return await GetAsync(entity.Id, branch, ct);
    }

    public async Task DeleteAsync(
        long id, string branchCode, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var entity = await Mappings.Query(tracking: true)
            .FirstOrDefaultAsync(x => x.Id == id && x.BranchCode == branch, ct)
            ?? throw AppException.NotFound("Tedarikçi stok eşlemesi bulunamadı.");
        var oldValues = Snapshot(entity);
        await Mappings.SoftDeleteAsync(id, ct);
        await SaveAsync(ct);
        await audit.WriteAsync(new AuditLogWriteEntry(
            "supplier-stock-mapping.delete", nameof(SupplierStockMapping),
            id.ToString(), "Succeeded", "goods-receipt",
            OldValues: oldValues,
            ChangedFields: ["IsDeleted"]), ct);
    }

    public async Task<SupplierStockResolution?> ResolveAsync(
        string branchCode, long supplierId, string supplierStockCode,
        CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var code = NormalizeCode(supplierStockCode);
        if (code.Length == 0) return null;
        return await (from mapping in Mappings.Query()
                      join stock in unitOfWork.Repository<StockEntity>().Query()
                          on mapping.StockId equals stock.Id
                      where mapping.BranchCode == branch
                            && mapping.SupplierId == supplierId
                            && mapping.NormalizedSupplierStockCode == code
                            && mapping.IsActive
                      select new SupplierStockResolution(
                          mapping.Id, mapping.SupplierId, stock.Id,
                          stock.ErpStockCode, stock.StockName, stock.BaseUnitCode,
                          mapping.ConversionFactor))
            .SingleOrDefaultAsync(ct);
    }

    private IQueryable<SupplierStockMappingRow> BuildRows(string branchCode) =>
        from mapping in Mappings.Query()
        join supplier in unitOfWork.Repository<CustomerEntity>().Query()
            on mapping.SupplierId equals supplier.Id
        join stock in unitOfWork.Repository<StockEntity>().Query()
            on mapping.StockId equals stock.Id
        where mapping.BranchCode == branchCode
        select new SupplierStockMappingRow
        {
            Id = mapping.Id,
            BranchCode = mapping.BranchCode,
            SupplierId = supplier.Id,
            SupplierCode = supplier.CustomerCode,
            SupplierName = supplier.CustomerName,
            SupplierStockCode = mapping.SupplierStockCode,
            SupplierStockName = mapping.SupplierStockName,
            SupplierUnitCode = mapping.SupplierUnitCode,
            StockId = stock.Id,
            SystemStockCode = stock.ErpStockCode,
            SystemStockName = stock.StockName,
            SystemUnitCode = stock.BaseUnitCode,
            ConversionFactor = mapping.ConversionFactor,
            IsActive = mapping.IsActive,
            Notes = mapping.Notes,
            CreatedBy = mapping.CreatedBy,
            CreatedDate = mapping.CreatedDate,
            UpdatedBy = mapping.UpdatedBy,
            UpdatedDate = mapping.UpdatedDate,
            RowVersion = mapping.RowVersion,
            SupplierSearchText = supplier.CustomerCode + " " + supplier.CustomerName,
            SystemStockSearchText = stock.ErpStockCode + " " + stock.StockName
        };

    private async Task<NormalizedRequest> ValidateAsync(
        SaveSupplierStockMappingRequest request, long? currentId, CancellationToken ct)
    {
        var branch = NormalizeBranch(request.BranchCode);
        var supplierStockCode = request.SupplierStockCode?.Trim() ?? string.Empty;
        var normalizedCode = NormalizeCode(supplierStockCode);
        if (normalizedCode.Length is < 1 or > 100)
            throw AppException.BadRequest("Tedarikçi stok kodu 1-100 karakter olmalıdır.");
        if (request.SupplierStockName?.Trim().Length > 500)
            throw AppException.BadRequest("Tedarikçi stok adı en fazla 500 karakter olabilir.");
        if (request.SupplierUnitCode?.Trim().Length > 20)
            throw AppException.BadRequest("Tedarikçi birimi en fazla 20 karakter olabilir.");
        var supplierUnitCode = Clean(request.SupplierUnitCode, 20, uppercase: true);
        if (request.ConversionFactor <= 0 || request.ConversionFactor > 1_000_000_000m)
            throw AppException.BadRequest("Birim dönüşüm katsayısı sıfırdan büyük olmalıdır.");
        if (request.Notes?.Trim().Length > 1000)
            throw AppException.BadRequest("Not en fazla 1000 karakter olabilir.");

        var supplierExists = await unitOfWork.Repository<CustomerEntity>().AnyAsync(
            x => x.Id == request.SupplierId && x.BranchCode == branch, ct);
        if (!supplierExists)
            throw AppException.BadRequest("Tedarikçi giriş yapılan şubede bulunamadı.");
        var stockExists = await unitOfWork.Repository<StockEntity>().AnyAsync(
            x => x.Id == request.StockId && x.BranchCode == branch, ct);
        if (!stockExists)
            throw AppException.BadRequest("Sistem stoğu giriş yapılan şubede bulunamadı.");
        if (await Mappings.AnyAsync(x => x.Id != currentId
                && x.BranchCode == branch
                && x.SupplierId == request.SupplierId
                && x.NormalizedSupplierStockCode == normalizedCode, ct))
            throw AppException.Conflict(
                "Bu tedarikçinin stok kodu zaten bir sistem stoğuyla eşlenmiş.");

        return new NormalizedRequest(
            branch, supplierStockCode, normalizedCode,
            Clean(request.SupplierStockName, 500),
            supplierUnitCode, Clean(request.Notes, 1000));
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await unitOfWork.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict(
                "Eşleme başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyip tekrar deneyin.");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException?.Message.Contains(
                "UX_RII_SUPPLIER_STOCK_MAPPING_IDENTITY",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            throw AppException.Conflict(
                "Bu tedarikçinin stok kodu zaten bir sistem stoğuyla eşlenmiş.");
        }
    }

    private async Task WriteAuditAsync(
        string action, SupplierStockMapping entity, object? oldValues, CancellationToken ct) =>
        await audit.WriteAsync(new AuditLogWriteEntry(
            action, nameof(SupplierStockMapping), entity.Id.ToString(),
            "Succeeded", "goods-receipt",
            OldValues: oldValues, NewValues: Snapshot(entity),
            ChangedFields:
            [
                "SupplierId", "SupplierStockCode", "SupplierStockName",
                "SupplierUnitCode", "StockId", "ConversionFactor",
                "IsActive", "Notes"
            ]), ct);

    private static void EnsureRowVersion(
        SupplierStockMapping entity, byte[]? supplied)
    {
        if (supplied is null || supplied.Length == 0)
            throw AppException.Conflict(
                "Güncelleme için satır sürümü zorunludur. Listeyi yenileyin.");
        if (!entity.RowVersion.SequenceEqual(supplied))
            throw AppException.Conflict(
                "Eşleme başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyip tekrar deneyin.");
    }

    private static string NormalizeBranch(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
    internal static string NormalizeCode(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string? Clean(string? value, int maxLength, bool uppercase = false)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result)) return null;
        if (result.Length > maxLength) result = result[..maxLength];
        return uppercase ? result.ToUpperInvariant() : result;
    }

    private static object Snapshot(SupplierStockMapping entity) => new
    {
        entity.Id, entity.BranchCode, entity.SupplierId,
        entity.SupplierStockCode, entity.SupplierStockName,
        entity.SupplierUnitCode, entity.StockId,
        entity.ConversionFactor, entity.IsActive, entity.Notes
    };

    private sealed record NormalizedRequest(
        string BranchCode,
        string SupplierStockCode,
        string NormalizedSupplierStockCode,
        string? SupplierStockName,
        string? SupplierUnitCode,
        string? Notes);
}
