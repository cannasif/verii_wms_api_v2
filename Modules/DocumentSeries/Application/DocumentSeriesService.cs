using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Localization;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using SeriesEntity = verii_wms_api_v2.Modules.DocumentSeries.Domain.DocumentSeries;

namespace verii_wms_api_v2.Modules.DocumentSeries.Application;

public sealed partial class DocumentSeriesService(
    IUnitOfWork unitOfWork,
    IAuditLogWriter audit,
    IStringLocalizer<DocumentSeriesResource> localizer) : IDocumentSeriesService
{
    private static readonly IReadOnlyDictionary<string, string> SearchColumnMapping =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nameof(DocumentSeriesGridRow.Id),
            ["branchCode"] = nameof(DocumentSeriesGridRow.BranchCode),
            ["code"] = nameof(DocumentSeriesGridRow.Code),
            ["name"] = nameof(DocumentSeriesGridRow.Name),
            ["prefix"] = nameof(DocumentSeriesGridRow.Prefix),
            ["documentType"] = nameof(DocumentSeriesGridRow.DocumentType),
            ["warehouseName"] = nameof(DocumentSeriesGridRow.WarehouseSearchText),
            ["nextNumber"] = nameof(DocumentSeriesGridRow.NextNumber),
            ["createdBy"] = nameof(DocumentSeriesGridRow.CreatedBySearchText),
            ["updatedBy"] = nameof(DocumentSeriesGridRow.UpdatedBySearchText)
        };
    private static readonly string[] DefaultSearchColumns = ["code", "name"];

    private IGenericRepository<SeriesEntity> Series => unitOfWork.Repository<SeriesEntity>();
    private IGenericRepository<WarehouseEntity> Warehouses => unitOfWork.Repository<WarehouseEntity>();
    private IGenericRepository<User> Users => unitOfWork.Repository<User>();
    private IGenericRepository<UserDetail> UserDetails => unitOfWork.Repository<UserDetail>();

    public async Task<PagedResponse<DocumentSeriesGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        return await BuildPagedQuery(request).ToPagedResponseAsync(request, cancellationToken);
    }

    internal IQueryable<DocumentSeriesGridRow> BuildPagedQuery(PagedRequest request) =>
        BuildGridQuery()
            .ApplySearch(request, SearchColumnMapping, DefaultSearchColumns)
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(DocumentSeriesGridRow.Code));

    public async Task<DocumentSeriesGridRow> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        await BuildGridQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw AppException.NotFound(Message(DocumentSeriesMessageKeys.NotFound));

    public async Task<IReadOnlyList<DocumentSeriesLookupRow>> GetLookupAsync(WmsDocumentType documentType, long? warehouseId, CancellationToken cancellationToken = default)
    {
        var rows = await Series.Query().Where(x => x.IsActive && x.DocumentType == documentType
                && (!x.WarehouseId.HasValue || x.WarehouseId == warehouseId))
            .OrderByDescending(x => x.WarehouseId == warehouseId)
            .ThenByDescending(x => x.IsDefault)
            .ThenBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name, x.Prefix, x.Separator, x.YearFormat, x.NumberLength, x.NextNumber, x.IsDefault })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new DocumentSeriesLookupRow(
            x.Id, x.Code, x.Name, FormatNumber(x.Prefix, x.Separator, x.YearFormat, x.NumberLength, x.NextNumber, DateTime.UtcNow), x.IsDefault)).ToList();
    }

    public async Task<long> CreateAsync(DocumentSeriesUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var value = await ValidateAsync(request, null, cancellationToken);
        var entity = new SeriesEntity();
        Apply(entity, request, value, allowNumberingChanges: true);
        await Series.AddAsync(entity, cancellationToken);
        await SaveAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("document-series.create", "DocumentSeries", entity.Id.ToString(), "Succeeded", "document-series", NewValues: Snapshot(entity), ChangedFields: Fields), cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(long id, DocumentSeriesUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await Series.FindByIdAsync(id, tracking: true, cancellationToken)
            ?? throw AppException.NotFound(Message(DocumentSeriesMessageKeys.NotFound));
        var value = await ValidateAsync(request, id, cancellationToken);
        if (entity.HasIssuedNumbers && NumberingIdentityChanged(entity, request, value))
            throw AppException.Conflict(Message(DocumentSeriesMessageKeys.IssuedSeriesImmutable));

        var oldValues = Snapshot(entity);
        Apply(entity, request, value, allowNumberingChanges: !entity.HasIssuedNumbers);
        await SaveAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("document-series.update", "DocumentSeries", id.ToString(), "Succeeded", "document-series", OldValues: oldValues, NewValues: Snapshot(entity), ChangedFields: Fields), cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await Series.FindByIdAsync(id, tracking: true, cancellationToken)
            ?? throw AppException.NotFound(Message(DocumentSeriesMessageKeys.NotFound));
        if (entity.HasIssuedNumbers) throw AppException.Conflict(Message(DocumentSeriesMessageKeys.IssuedSeriesDeleteBlocked));
        var oldValues = Snapshot(entity);
        entity.IsActive = false;
        await Series.SoftDeleteAsync(id, cancellationToken);
        await SaveAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("document-series.delete", "DocumentSeries", id.ToString(), "Succeeded", "document-series", OldValues: oldValues, ChangedFields: ["IsDeleted", "IsActive"]), cancellationToken);
    }

    private IQueryable<DocumentSeriesGridRow> BuildGridQuery()
    {
        var series = Series.Query();
        var warehouses = Warehouses.Query();
        var users = Users.Query();
        var userDetails = UserDetails.Query();
        return from item in series
               join warehouse in warehouses on item.WarehouseId equals warehouse.Id into warehouseRows
               from warehouse in warehouseRows.DefaultIfEmpty()
               join createdUser in users on item.CreatedBy equals (long?)createdUser.Id into createdUsers
               from createdUser in createdUsers.DefaultIfEmpty()
               join createdDetail in userDetails on item.CreatedBy equals (long?)createdDetail.UserId into createdDetails
               from createdDetail in createdDetails.DefaultIfEmpty()
               join updatedUser in users on item.UpdatedBy equals (long?)updatedUser.Id into updatedUsers
               from updatedUser in updatedUsers.DefaultIfEmpty()
               join updatedDetail in userDetails on item.UpdatedBy equals (long?)updatedDetail.UserId into updatedDetails
               from updatedDetail in updatedDetails.DefaultIfEmpty()
               select new DocumentSeriesGridRow
               {
                   Id = item.Id,
                   BranchCode = item.BranchCode,
                   WarehouseId = item.WarehouseId,
                   WarehouseCode = warehouse == null ? null : warehouse.WarehouseCode,
                   WarehouseName = warehouse == null ? null : warehouse.WarehouseName,
                   WarehouseSearchText = warehouse == null ? null : warehouse.WarehouseCode + " " + warehouse.WarehouseName,
                   Code = item.Code,
                   Name = item.Name,
                   DocumentType = item.DocumentType == WmsDocumentType.GoodsReceipt ? "GoodsReceipt"
                       : item.DocumentType == WmsDocumentType.InterWarehouseTransfer ? "InterWarehouseTransfer"
                       : item.DocumentType == WmsDocumentType.Shipment ? "Shipment"
                       : item.DocumentType == WmsDocumentType.WarehouseReceipt ? "WarehouseReceipt"
                       : item.DocumentType == WmsDocumentType.WarehouseIssue ? "WarehouseIssue"
                       : item.DocumentType == WmsDocumentType.ProductionTransfer ? "ProductionTransfer"
                       : item.DocumentType == WmsDocumentType.SubcontractingIssue ? "SubcontractingIssue"
                       : "SubcontractingReceipt",
                   Prefix = item.Prefix,
                   Separator = item.Separator,
                   YearFormat = item.YearFormat == DocumentYearFormat.None ? "None"
                       : item.YearFormat == DocumentYearFormat.TwoDigit ? "TwoDigit" : "FourDigit",
                   NumberLength = item.NumberLength,
                   StartNumber = item.StartNumber,
                   NextNumber = item.NextNumber,
                   IncrementBy = item.IncrementBy,
                   IsDefault = item.IsDefault,
                   IsActive = item.IsActive,
                   HasIssuedNumbers = item.HasIssuedNumbers,
                   LastIssuedAt = item.LastIssuedAt,
                   Description = item.Description,
                   CreatedBy = item.CreatedBy,
                   CreatedByName = createdUser == null
                       ? null
                       : createdDetail != null && (createdDetail.FirstName != "" || createdDetail.LastName != "")
                           ? (createdDetail.FirstName + " " + createdDetail.LastName).Trim()
                           : createdUser.Username,
                   CreatedBySearchText = createdUser == null
                       ? null
                       : createdUser.Username + " " + createdUser.Email + " "
                           + (createdDetail == null ? "" : createdDetail.FirstName + " " + createdDetail.LastName),
                   CreatedDate = item.CreatedDate,
                   UpdatedBy = item.UpdatedBy,
                   UpdatedByName = updatedUser == null
                       ? null
                       : updatedDetail != null && (updatedDetail.FirstName != "" || updatedDetail.LastName != "")
                           ? (updatedDetail.FirstName + " " + updatedDetail.LastName).Trim()
                           : updatedUser.Username,
                   UpdatedBySearchText = updatedUser == null
                       ? null
                       : updatedUser.Username + " " + updatedUser.Email + " "
                           + (updatedDetail == null ? "" : updatedDetail.FirstName + " " + updatedDetail.LastName),
                   UpdatedDate = item.UpdatedDate
               };
    }

    private async Task<NormalizedRequest> ValidateAsync(DocumentSeriesUpsertRequest request, long? currentId, CancellationToken cancellationToken)
    {
        var branchCode = string.IsNullOrWhiteSpace(request.BranchCode) ? "0" : request.BranchCode.Trim();
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var prefix = request.Prefix?.Trim().ToUpperInvariant() ?? string.Empty;
        var separator = request.Separator?.Trim() ?? string.Empty;
        if (!CodePattern().IsMatch(code)) throw AppException.BadRequest(Message(DocumentSeriesMessageKeys.InvalidCode));
        if (name.Length is < 2 or > 150) throw AppException.BadRequest(Message(DocumentSeriesMessageKeys.InvalidName));
        if (!PrefixPattern().IsMatch(prefix)) throw AppException.BadRequest(Message(DocumentSeriesMessageKeys.InvalidPrefix));
        if (separator.Length > 3 || separator.Any(char.IsWhiteSpace)) throw AppException.BadRequest(Message(DocumentSeriesMessageKeys.InvalidSeparator));
        if (request.NumberLength is < 3 or > 18 || request.StartNumber < 1 || request.NextNumber < request.StartNumber || request.IncrementBy is < 1 or > 1000)
            throw AppException.BadRequest(Message(DocumentSeriesMessageKeys.InvalidNumberSettings));
        if (request.Description?.Length > 500) throw AppException.BadRequest(Message(DocumentSeriesMessageKeys.InvalidName));

        if (request.WarehouseId.HasValue)
        {
            var warehouse = await Warehouses.Query().Where(x => x.Id == request.WarehouseId.Value)
                .Select(x => new { x.Id, x.BranchCode }).FirstOrDefaultAsync(cancellationToken)
                ?? throw AppException.BadRequest(Message(DocumentSeriesMessageKeys.WarehouseNotFound));
            branchCode = warehouse.BranchCode;
        }

        if (await Series.AnyAsync(x => x.Id != currentId && x.BranchCode == branchCode && x.DocumentType == request.DocumentType && x.Code == code, cancellationToken))
            throw AppException.Conflict(Message(DocumentSeriesMessageKeys.DuplicateCode));
        if (request.IsDefault && request.IsActive && await Series.AnyAsync(x => x.Id != currentId && x.BranchCode == branchCode
                && x.DocumentType == request.DocumentType && x.WarehouseId == request.WarehouseId && x.IsDefault && x.IsActive, cancellationToken))
            throw AppException.Conflict(Message(DocumentSeriesMessageKeys.DuplicateDefault));

        return new NormalizedRequest(branchCode, code, name, prefix, separator);
    }

    private static void Apply(SeriesEntity entity, DocumentSeriesUpsertRequest request, NormalizedRequest value, bool allowNumberingChanges)
    {
        entity.Name = value.Name;
        entity.IsDefault = request.IsDefault;
        entity.IsActive = request.IsActive;
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (!allowNumberingChanges) return;
        entity.BranchCode = value.BranchCode;
        entity.WarehouseId = request.WarehouseId;
        entity.Code = value.Code;
        entity.DocumentType = request.DocumentType;
        entity.Prefix = value.Prefix;
        entity.Separator = value.Separator;
        entity.YearFormat = request.YearFormat;
        entity.NumberLength = request.NumberLength;
        entity.StartNumber = request.StartNumber;
        entity.NextNumber = request.NextNumber;
        entity.IncrementBy = request.IncrementBy;
    }

    private static bool NumberingIdentityChanged(SeriesEntity entity, DocumentSeriesUpsertRequest request, NormalizedRequest value) =>
        entity.BranchCode != value.BranchCode || entity.WarehouseId != request.WarehouseId || entity.Code != value.Code
        || entity.DocumentType != request.DocumentType || entity.Prefix != value.Prefix || entity.Separator != value.Separator
        || entity.YearFormat != request.YearFormat || entity.NumberLength != request.NumberLength || entity.StartNumber != request.StartNumber
        || entity.NextNumber != request.NextNumber || entity.IncrementBy != request.IncrementBy;

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict(Message(DocumentSeriesMessageKeys.ConcurrencyConflict)); }
    }

    internal static string FormatNumber(string prefix, string separator, DocumentYearFormat yearFormat, int numberLength, long number, DateTime issuedAt)
    {
        var year = yearFormat switch { DocumentYearFormat.TwoDigit => issuedAt.ToString("yy"), DocumentYearFormat.FourDigit => issuedAt.ToString("yyyy"), _ => string.Empty };
        return string.IsNullOrEmpty(year)
            ? $"{prefix}{separator}{number.ToString().PadLeft(numberLength, '0')}"
            : $"{prefix}{separator}{year}{separator}{number.ToString().PadLeft(numberLength, '0')}";
    }

    private static object Snapshot(SeriesEntity x) => new { x.Id, x.BranchCode, x.WarehouseId, x.Code, x.Name, x.DocumentType, x.Prefix, x.Separator, x.YearFormat, x.NumberLength, x.StartNumber, x.NextNumber, x.IncrementBy, x.IsDefault, x.IsActive, x.HasIssuedNumbers, x.LastIssuedAt, x.Description };
    private string Message(string key) => localizer[key].Value;
    private static readonly string[] Fields = ["BranchCode", "WarehouseId", "Code", "Name", "DocumentType", "Prefix", "Separator", "YearFormat", "NumberLength", "StartNumber", "NextNumber", "IncrementBy", "IsDefault", "IsActive", "Description"];
    private sealed record NormalizedRequest(string BranchCode, string Code, string Name, string Prefix, string Separator);
    [GeneratedRegex("^[A-Z0-9_-]{2,20}$", RegexOptions.CultureInvariant)] private static partial Regex CodePattern();
    [GeneratedRegex("^[A-Z0-9]{1,10}$", RegexOptions.CultureInvariant)] private static partial Regex PrefixPattern();
}
