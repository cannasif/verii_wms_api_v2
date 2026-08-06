using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed class KkdDefinitionService(IUnitOfWork uow) : IKkdDefinitionService
{
    public async Task<IReadOnlyList<KkdLookupRow>> GetDepartmentsAsync(CancellationToken ct = default) =>
        await uow.Repository<KkdDepartment>().Query().OrderBy(x => x.Name)
            .Select(x => new KkdLookupRow(x.Id, x.Code, x.Name, x.IsActive)).ToListAsync(ct);

    public async Task<IReadOnlyList<KkdLookupRow>> GetRolesAsync(long? departmentId, CancellationToken ct = default) =>
        await uow.Repository<KkdRole>().Query().Where(x => !departmentId.HasValue || x.DepartmentId == departmentId)
            .OrderBy(x => x.Name).Select(x => new KkdLookupRow(x.Id, x.Code, x.Name, x.IsActive)).ToListAsync(ct);

    public Task<PagedResponse<KkdCustomerLookupRow>> GetCustomersPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var query = uow.Repository<CustomerEntity>().Query()
            .Select(x => new KkdCustomerLookupRow(x.Id, x.CustomerCode, x.CustomerName))
            .ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = nameof(KkdCustomerLookupRow.Code),
                ["name"] = nameof(KkdCustomerLookupRow.Name)
            }, ["code", "name"])
            .ApplySort(request, nameof(KkdCustomerLookupRow.Code));
        return query.ToPagedResponseAsync(request, ct);
    }

    public Task<PagedResponse<KkdStockLookupRow>> GetStocksPagedAsync(PagedRequest request, string? groupCode, CancellationToken ct = default)
    {
        var normalizedGroup = Normalize(groupCode);
        var stocks = uow.Repository<StockEntity>().Query()
            .Where(x => normalizedGroup.Length == 0 || x.GroupCode == normalizedGroup);
        var query = stocks
            .Select(x => new KkdStockLookupRow(x.Id, x.ErpStockCode, x.StockName, x.BaseUnitCode, x.GroupCode))
            .ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = nameof(KkdStockLookupRow.Code),
                ["name"] = nameof(KkdStockLookupRow.Name)
            }, ["code", "name"])
            .ApplySort(request, nameof(KkdStockLookupRow.Code));
        return query.ToPagedResponseAsync(request, ct);
    }

    public Task<PagedResponse<KkdStockGroupLookupRow>> GetStockGroupsPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var groups = uow.Repository<StockEntity>().Query()
            .Where(x => x.GroupCode != null && x.GroupCode != string.Empty)
            .GroupBy(x => x.GroupCode!)
            .Select(x => new KkdStockGroupLookupRow(x.Key, x.Count()))
            .ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = nameof(KkdStockGroupLookupRow.Code)
            }, ["code"])
            .ApplySort(request, nameof(KkdStockGroupLookupRow.Code));
        return groups.ToPagedResponseAsync(request, ct);
    }

    public Task<PagedResponse<KkdEntitlementGroupLookupRow>> GetEntitlementGroupsPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var groups = uow.Repository<KkdEntitlementRule>().Query()
            .Where(x => x.GroupCode != string.Empty)
            .GroupBy(x => x.GroupCode)
            .Select(x => new KkdEntitlementGroupLookupRow(
                x.Key,
                x.Select(r => r.GroupName).FirstOrDefault(name => name != null && name != string.Empty) ?? x.Key,
                x.Count()))
            .ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["code"] = nameof(KkdEntitlementGroupLookupRow.Code),
                ["name"] = nameof(KkdEntitlementGroupLookupRow.Name)
            }, ["code", "name"])
            .ApplySort(request, nameof(KkdEntitlementGroupLookupRow.Code));
        return groups.ToPagedResponseAsync(request, ct);
    }

    public async Task<IReadOnlyList<KkdEmployeeRow>> GetEmployeesAsync(CancellationToken ct = default) =>
        await uow.Repository<KkdEmployee>().Query().OrderBy(x => x.EmployeeCode)
            .Select(x => new KkdEmployeeRow(x.Id, x.EmployeeCode, x.FirstName + " " + x.LastName, x.QrCode, x.CustomerId,
                x.DepartmentId, x.Department.Name, x.RoleId, x.Role.Name, x.EmploymentStartDate, x.IsActive)).ToListAsync(ct);

    public async Task<KkdEmployeeRow> ResolveEmployeeByQrAsync(string qrCode, CancellationToken ct = default)
    {
        var normalized = RequiredCode(qrCode, "Personel QR kodu", 200);
        return await uow.Repository<KkdEmployee>().Query()
            .Where(x => x.QrCode == normalized && x.IsActive)
            .Select(x => new KkdEmployeeRow(x.Id, x.EmployeeCode, x.FirstName + " " + x.LastName, x.QrCode, x.CustomerId,
                x.DepartmentId, x.Department.Name, x.RoleId, x.Role.Name, x.EmploymentStartDate, x.IsActive))
            .SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("Aktif KKD personeli QR koduyla bulunamadı.");
    }

    public async Task<IReadOnlyList<KkdMatrixRow>> GetMatricesAsync(CancellationToken ct = default) =>
        await uow.Repository<KkdEntitlementMatrix>().Query().OrderBy(x => x.Code)
            .Select(x => new KkdMatrixRow(x.Id, x.Code, x.Name, x.CustomerId, x.DepartmentId, x.RoleId,
                x.EffectiveFrom, x.EffectiveTo, x.IsActive, x.Rules.Count(r => !r.IsDeleted))).ToListAsync(ct);

    public async Task<KkdMatrixDetail> GetMatrixAsync(long id, CancellationToken ct = default)
    {
        var matrix = await uow.Repository<KkdEntitlementMatrix>().Query()
            .Where(x => x.Id == id)
            .Select(x => new KkdMatrixDetail(
                x.Id, x.CustomerId, x.DepartmentId, x.RoleId, x.Code, x.Name, x.EffectiveFrom, x.EffectiveTo,
                x.IsActive, x.Description,
                x.Rules.Where(r => !r.IsDeleted).OrderBy(r => r.SortOrder).Select(r => new KkdRuleDetail(
                    r.Id, r.GroupCode, r.GroupName, r.StockId, r.StockCodeSnapshot, r.StockNameSnapshot,
                    r.StandardCode, r.StandardName, r.AnnualIssueCount, r.AnnualQuantity, r.MaxCarryQuantity,
                    r.AllowBulkIssue, r.IsMandatory, r.SortOrder, r.IsActive, r.Description,
                    r.Phases.Where(p => !p.IsDeleted).OrderBy(p => p.SortOrder).Select(p => new KkdPhaseDetail(
                        p.Id, p.PhaseType.ToString(), p.OffsetMonths, p.Quantity, p.AllowBulkIssue,
                        p.FrequencyDays, p.QuantityPerFrequency, p.PeriodType == null ? null : p.PeriodType.ToString(),
                        p.PeriodInterval, p.SortOrder, p.IsActive, p.Description)).ToList())).ToList()))
            .SingleOrDefaultAsync(ct);
        return matrix ?? throw AppException.NotFound("KKD hak matrisi bulunamadı.");
    }

    public async Task<long> UpsertDepartmentAsync(long? id, KkdDepartmentUpsertRequest request, long actor, CancellationToken ct = default)
    {
        var code = RequiredCode(request.Code, "Departman kodu", 50);
        var name = RequiredText(request.Name, "Departman adı", 200);
        var repository = uow.Repository<KkdDepartment>();
        if (await repository.AnyAsync(x => x.Code == code && (!id.HasValue || x.Id != id), ct))
            throw AppException.Conflict("Aynı departman kodu zaten tanımlı.");
        var entity = id.HasValue ? await repository.FindByIdAsync(id.Value, true, ct)
            ?? throw AppException.NotFound("Departman bulunamadı.") : new KkdDepartment();
        entity.Code = code; entity.Name = name; entity.IsActive = request.IsActive;
        Touch(entity, actor, id.HasValue);
        if (!id.HasValue) await repository.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<long> UpsertRoleAsync(long? id, KkdRoleUpsertRequest request, long actor, CancellationToken ct = default)
    {
        if (request.DepartmentId.HasValue && !await uow.Repository<KkdDepartment>().AnyAsync(x => x.Id == request.DepartmentId && x.IsActive, ct))
            throw AppException.BadRequest("Seçilen departman bulunamadı veya aktif değil.");
        var code = RequiredCode(request.Code, "Rol kodu", 50);
        var repository = uow.Repository<KkdRole>();
        if (await repository.AnyAsync(x => x.DepartmentId == request.DepartmentId && x.Code == code && (!id.HasValue || x.Id != id), ct))
            throw AppException.Conflict("Departman içinde aynı rol kodu zaten tanımlı.");
        var entity = id.HasValue ? await repository.FindByIdAsync(id.Value, true, ct)
            ?? throw AppException.NotFound("Rol bulunamadı.") : new KkdRole();
        entity.DepartmentId = request.DepartmentId; entity.Code = code;
        entity.Name = RequiredText(request.Name, "Rol adı", 200); entity.IsActive = request.IsActive;
        Touch(entity, actor, id.HasValue);
        if (!id.HasValue) await repository.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<long> UpsertEmployeeAsync(long? id, KkdEmployeeUpsertRequest request, long actor, CancellationToken ct = default)
    {
        if (!await uow.Repository<CustomerEntity>().AnyAsync(x => x.Id == request.CustomerId, ct))
            throw AppException.BadRequest("Seçilen entegre cari bulunamadı.");
        var department = await uow.Repository<KkdDepartment>().FindByIdAsync(request.DepartmentId, false, ct)
            ?? throw AppException.BadRequest("Departman bulunamadı.");
        var role = await uow.Repository<KkdRole>().FindByIdAsync(request.RoleId, false, ct)
            ?? throw AppException.BadRequest("Rol bulunamadı.");
        if (!department.IsActive || !role.IsActive || (role.DepartmentId.HasValue && role.DepartmentId != department.Id))
            throw AppException.BadRequest("Personel departman ve rol eşleşmesi geçersiz.");
        var code = RequiredCode(request.EmployeeCode, "Personel kodu", 80);
        var qr = RequiredCode(request.QrCode, "QR kodu", 200);
        var repository = uow.Repository<KkdEmployee>();
        if (await repository.AnyAsync(x => (x.EmployeeCode == code || x.QrCode == qr) && (!id.HasValue || x.Id != id), ct))
            throw AppException.Conflict("Personel kodu veya QR kodu daha önce kullanılmış.");
        var entity = id.HasValue ? await repository.FindByIdAsync(id.Value, true, ct)
            ?? throw AppException.NotFound("KKD personeli bulunamadı.") : new KkdEmployee();
        entity.CustomerId = request.CustomerId; entity.UserId = request.UserId; entity.EmployeeCode = code;
        entity.FirstName = RequiredText(request.FirstName, "Ad", 100); entity.LastName = RequiredText(request.LastName, "Soyad", 100);
        entity.DepartmentId = department.Id; entity.RoleId = role.Id; entity.QrCode = qr;
        entity.EmploymentStartDate = request.EmploymentStartDate; entity.IsActive = request.IsActive;
        Touch(entity, actor, id.HasValue);
        if (!id.HasValue) await repository.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        return entity.Id;
    }

    public Task<long> UpsertMatrixAsync(long? id, KkdMatrixUpsertRequest request, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(token => UpsertMatrixCoreAsync(id, request, actor, token), ct);

    private async Task<long> UpsertMatrixCoreAsync(long? id, KkdMatrixUpsertRequest request, long actor, CancellationToken ct)
    {
        if (request.Rules.Count == 0) throw AppException.BadRequest("Hak matrisi en az bir kural içermelidir.");
        if (request.EffectiveTo.HasValue && request.EffectiveFrom.HasValue && request.EffectiveTo < request.EffectiveFrom)
            throw AppException.BadRequest("Matris bitiş tarihi başlangıç tarihinden önce olamaz.");
        if (!await uow.Repository<KkdDepartment>().AnyAsync(x => x.Id == request.DepartmentId && x.IsActive, ct)
            || !await uow.Repository<KkdRole>().AnyAsync(x => x.Id == request.RoleId && x.IsActive, ct))
            throw AppException.BadRequest("Aktif departman ve rol seçilmelidir.");
        if (!await uow.Repository<CustomerEntity>().AnyAsync(x => x.Id == request.CustomerId, ct))
            throw AppException.BadRequest("Seçilen entegre cari bulunamadı.");

        var code = RequiredCode(request.Code, "Matris kodu", 80);
        var repository = uow.Repository<KkdEntitlementMatrix>();
        if (await repository.AnyAsync(x => x.Code == code && (!id.HasValue || x.Id != id), ct))
            throw AppException.Conflict("Aynı matris kodu zaten tanımlı.");
        if (request.IsActive && await repository.AnyAsync(x => x.CustomerId == request.CustomerId
            && x.DepartmentId == request.DepartmentId && x.RoleId == request.RoleId && x.IsActive
            && (!id.HasValue || x.Id != id)
            && (!x.EffectiveTo.HasValue || !request.EffectiveFrom.HasValue || x.EffectiveTo >= request.EffectiveFrom)
            && (!request.EffectiveTo.HasValue || !x.EffectiveFrom.HasValue || request.EffectiveTo >= x.EffectiveFrom), ct))
            throw AppException.Conflict("Aynı cari, departman ve rol için tarihleri çakışan aktif bir KKD matrisi var.");

        var entity = id.HasValue
            ? await repository.Query(true).Include(x => x.Rules).ThenInclude(x => x.Phases).SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw AppException.NotFound("KKD matrisi bulunamadı.")
            : new KkdEntitlementMatrix();
        var now = DateTime.UtcNow;
        foreach (var oldRule in entity.Rules.Where(x => !x.IsDeleted))
        {
            oldRule.IsDeleted = true; oldRule.DeletedBy = actor; oldRule.DeletedDate = now;
            foreach (var oldPhase in oldRule.Phases.Where(x => !x.IsDeleted))
            { oldPhase.IsDeleted = true; oldPhase.DeletedBy = actor; oldPhase.DeletedDate = now; }
        }

        entity.CustomerId = request.CustomerId; entity.DepartmentId = request.DepartmentId; entity.RoleId = request.RoleId;
        entity.Code = code; entity.Name = RequiredText(request.Name, "Matris adı", 200);
        entity.EffectiveFrom = request.EffectiveFrom; entity.EffectiveTo = request.EffectiveTo;
        entity.IsActive = request.IsActive; entity.Description = Clean(request.Description, 1000);
        Touch(entity, actor, id.HasValue);
        if (!id.HasValue) await repository.AddAsync(entity, ct);

        var duplicateKeys = request.Rules.GroupBy(x => $"{x.StockId?.ToString() ?? "GROUP"}|{Normalize(x.GroupCode)}")
            .Where(x => x.Count() > 1).Select(x => x.Key).ToArray();
        if (duplicateKeys.Length > 0) throw AppException.BadRequest("Matris içinde aynı stok/grup kuralı birden fazla kez tanımlanamaz.");

        foreach (var item in request.Rules)
        {
            StockEntity? stock = null;
            if (item.StockId.HasValue)
                stock = await uow.Repository<StockEntity>().FindByIdAsync(item.StockId.Value, false, ct)
                    ?? throw AppException.BadRequest($"{item.StockId} numaralı stok bulunamadı.");
            var groupCode = Normalize(item.GroupCode);
            var stockGroupCode = Normalize(stock?.GroupCode);
            if (stock is not null && groupCode.Length == 0)
                groupCode = stockGroupCode;
            if (groupCode.Length == 0)
                throw AppException.BadRequest("Stok seçilmediğinde hakediş grubu zorunludur.");
            // Stok-özel kuralda GroupCode, ERP stok grubundan bağımsız bir KKD hakediş kategorisi olabilir.
            // Grup bazlı kuralda ise kodun gerçekten ERP stok kartlarında karşılığı bulunmalıdır.
            if (stock is null && !await uow.Repository<StockEntity>().AnyAsync(x => x.GroupCode == groupCode, ct))
                throw AppException.BadRequest($"{groupCode} kodlu stok grubu bulunamadı.");
            var rule = new KkdEntitlementRule
            {
                BranchCode = entity.BranchCode,
                Matrix = entity, GroupCode = groupCode, GroupName = Clean(item.GroupName, 200) ?? groupCode,
                StockId = stock?.Id, StockCodeSnapshot = stock?.ErpStockCode, StockNameSnapshot = stock?.StockName,
                StandardCode = Clean(item.StandardCode, 80), StandardName = Clean(item.StandardName, 200),
                AnnualIssueCount = item.AnnualIssueCount, AnnualQuantity = item.AnnualQuantity, MaxCarryQuantity = item.MaxCarryQuantity,
                AllowBulkIssue = item.AllowBulkIssue, IsMandatory = item.IsMandatory, SortOrder = item.SortOrder,
                IsActive = item.IsActive, Description = Clean(item.Description, 1000), CreatedBy = actor, CreatedDate = now
            };
            if (item.Phases.Count == 0) throw AppException.BadRequest($"{rule.GroupCode} kuralında en az bir dönem olmalıdır.");
            foreach (var phaseRequest in item.Phases)
            {
                if (!Enum.TryParse<KkdEntitlementPhaseType>(phaseRequest.PhaseType, true, out var phaseType))
                    throw AppException.BadRequest($"Geçersiz KKD dönem tipi: {phaseRequest.PhaseType}");
                KkdPeriodType? periodType = null;
                if (!string.IsNullOrWhiteSpace(phaseRequest.PeriodType))
                {
                    if (!Enum.TryParse<KkdPeriodType>(phaseRequest.PeriodType, true, out var parsed))
                        throw AppException.BadRequest($"Geçersiz KKD periyot tipi: {phaseRequest.PeriodType}");
                    periodType = parsed;
                }
                rule.Phases.Add(new KkdEntitlementPhase
                {
                    BranchCode = entity.BranchCode,
                    Rule = rule, PhaseType = phaseType, OffsetMonths = phaseRequest.OffsetMonths,
                    Quantity = phaseRequest.Quantity, AllowBulkIssue = phaseRequest.AllowBulkIssue,
                    FrequencyDays = phaseRequest.FrequencyDays, QuantityPerFrequency = phaseRequest.QuantityPerFrequency,
                    PeriodType = periodType, PeriodInterval = phaseRequest.PeriodInterval, SortOrder = phaseRequest.SortOrder,
                    IsActive = phaseRequest.IsActive, Description = Clean(phaseRequest.Description, 1000),
                    CreatedBy = actor, CreatedDate = now
                });
            }
            entity.Rules.Add(rule);
        }
        await uow.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<long> CreateOverrideAsync(KkdOverrideCreateRequest request, long actor, CancellationToken ct = default)
    {
        if (request.Quantity <= 0) throw AppException.BadRequest("Ek hak miktarı sıfırdan büyük olmalıdır.");
        if (request.ValidTo.HasValue && request.ValidTo < request.ValidFrom) throw AppException.BadRequest("Ek hak tarih aralığı geçersiz.");
        if (!await uow.Repository<KkdEmployee>().AnyAsync(x => x.Id == request.EmployeeId && x.IsActive, ct))
            throw AppException.BadRequest("Aktif KKD personeli bulunamadı.");
        if (request.RuleId.HasValue && !await uow.Repository<KkdEntitlementRule>().AnyAsync(x => x.Id == request.RuleId && x.IsActive, ct))
            throw AppException.BadRequest("Aktif KKD kuralı bulunamadı.");
        var entity = new KkdEmployeeEntitlementOverride
        {
            EmployeeId = request.EmployeeId, RuleId = request.RuleId, GroupCode = Normalize(request.GroupCode),
            Quantity = request.Quantity, ValidFrom = request.ValidFrom, ValidTo = request.ValidTo,
            Reason = RequiredText(request.Reason, "Ek hak gerekçesi", 1000), ApprovedByUserId = actor,
            IsActive = request.IsActive, CreatedBy = actor, CreatedDate = DateTime.UtcNow
        };
        await uow.Repository<KkdEmployeeEntitlementOverride>().AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        return entity.Id;
    }

    private static string RequiredText(string? value, string field, int max)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result)) throw AppException.BadRequest($"{field} zorunludur.");
        if (result.Length > max) throw AppException.BadRequest($"{field} en fazla {max} karakter olabilir.");
        return result;
    }
    private static string RequiredCode(string? value, string field, int max) =>
        RequiredText(value, field, max).ToUpperInvariant();
    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static void Touch(verii_wms_api_v2.Shared.Domain.BaseEntity entity, long actor, bool exists)
    {
        if (exists) { entity.UpdatedBy = actor; entity.UpdatedDate = DateTime.UtcNow; }
        else { entity.CreatedBy = actor; entity.CreatedDate = DateTime.UtcNow; }
    }
}
