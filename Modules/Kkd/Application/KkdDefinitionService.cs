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

    public async Task<IReadOnlyList<KkdStockBulkResolveRow>> ResolveStocksAsync(KkdStockBulkResolveRequest request, CancellationToken ct = default)
    {
        if (request.Codes.Count > 5000) throw AppException.BadRequest("Tek işlemde en fazla 5.000 stok kodu çözümlenebilir.");
        var requested = request.Codes.Select(Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var found = new Dictionary<string, KkdStockLookupRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in requested.Chunk(1000))
        {
            var rows = await uow.Repository<StockEntity>().Query().Where(x => chunk.Contains(x.ErpStockCode))
                .Select(x => new KkdStockLookupRow(x.Id, x.ErpStockCode, x.StockName, x.BaseUnitCode, x.GroupCode)).ToListAsync(ct);
            foreach (var row in rows) found[row.Code] = row;
        }
        return requested.Select(code => found.TryGetValue(code, out var stock)
            ? new KkdStockBulkResolveRow(code, stock.Id, stock.Code, stock.Name, stock.UnitCode, stock.GroupCode, true)
            : new KkdStockBulkResolveRow(code, null, null, null, null, null, false)).ToArray();
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
                        p.PeriodInterval, p.SortOrder, p.IsActive, p.Description)).ToList())).ToList(), x.RowVersion))
            .SingleOrDefaultAsync(ct);
        return matrix ?? throw AppException.NotFound("KKD hak matrisi bulunamadı.");
    }

    public async Task<KkdMatrixValidationResult> ValidateMatrixAsync(long? id, KkdMatrixUpsertRequest request, CancellationToken ct = default)
    {
        const int maxRules = 5000;
        const int maxIssues = 500;
        var issues = new List<KkdMatrixValidationIssue>();
        void Add(int row, string field, string code, string message)
        {
            if (issues.Count < maxIssues) issues.Add(new(row, field, code, message));
        }

        if (request.Rules.Count == 0) Add(0, "rules", "REQUIRED", "Hak matrisi en az bir kural içermelidir.");
        if (request.Rules.Count > maxRules) Add(0, "rules", "LIMIT_EXCEEDED", $"Tek işlemde en fazla {maxRules:N0} kural işlenebilir.");
        if (request.EffectiveTo.HasValue && request.EffectiveFrom.HasValue && request.EffectiveTo < request.EffectiveFrom)
            Add(0, "effectiveTo", "INVALID_RANGE", "Matris bitiş tarihi başlangıç tarihinden önce olamaz.");
        if (!await uow.Repository<KkdDepartment>().AnyAsync(x => x.Id == request.DepartmentId && x.IsActive, ct))
            Add(0, "departmentId", "NOT_FOUND", "Aktif departman bulunamadı.");
        if (!await uow.Repository<KkdRole>().AnyAsync(x => x.Id == request.RoleId && x.IsActive, ct))
            Add(0, "roleId", "NOT_FOUND", "Aktif rol bulunamadı.");
        if (!await uow.Repository<CustomerEntity>().AnyAsync(x => x.Id == request.CustomerId, ct))
            Add(0, "customerId", "NOT_FOUND", "Seçilen entegre cari bulunamadı.");

        var stockIds = request.Rules.Where(x => x.StockId.HasValue).Select(x => x.StockId!.Value).Distinct().ToArray();
        var stocks = await LoadStocksAsync(stockIds, ct);
        var groupCodes = request.Rules.Where(x => !x.StockId.HasValue).Select(x => Normalize(x.GroupCode))
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var knownGroups = await LoadStockGroupsAsync(groupCodes, ct);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < request.Rules.Count; index++)
        {
            var row = index + 1;
            var rule = request.Rules[index];
            stocks.TryGetValue(rule.StockId ?? 0, out var stock);
            if (rule.StockId.HasValue && stock is null)
                Add(row, "stockId", "NOT_FOUND", $"{rule.StockId} numaralı stok bulunamadı.");
            var groupCode = Normalize(rule.GroupCode);
            if (groupCode.Length == 0 && stock is not null) groupCode = Normalize(stock.GroupCode);
            if (groupCode.Length == 0)
                Add(row, "groupCode", "REQUIRED", "Stok seçilmediğinde hakediş grubu zorunludur.");
            else if (stock is null && !rule.StockId.HasValue && !knownGroups.Contains(groupCode))
                Add(row, "groupCode", "NOT_FOUND", $"{groupCode} kodlu ERP stok grubu bulunamadı.");

            var key = $"{rule.StockId?.ToString() ?? "GROUP"}|{groupCode}";
            if (seen.TryGetValue(key, out var firstRow))
                Add(row, "stockId", "DUPLICATE", $"Aynı stok/grup kuralı {firstRow}. satırda zaten tanımlı.");
            else seen[key] = row;

            if (rule.AnnualIssueCount is <= 0) Add(row, "annualIssueCount", "INVALID", "Yıllık teslim sayısı sıfırdan büyük olmalıdır.");
            if (rule.AnnualQuantity is < 0) Add(row, "annualQuantity", "INVALID", "Yıllık miktar negatif olamaz.");
            if (rule.MaxCarryQuantity is < 0) Add(row, "maxCarryQuantity", "INVALID", "Devreden üst sınır negatif olamaz.");
            if (rule.Phases.Count == 0) Add(row, "phases", "REQUIRED", "En az bir dönem tanımlanmalıdır.");
            foreach (var phase in rule.Phases)
            {
                if (!Enum.TryParse<KkdEntitlementPhaseType>(phase.PhaseType, true, out _))
                    Add(row, "phaseType", "INVALID_ENUM", $"Geçersiz dönem tipi: {phase.PhaseType}");
                if (phase.Quantity < 0 || phase.OffsetMonths < 0)
                    Add(row, "quantity", "INVALID", "Dönem miktarı ve ay ofseti negatif olamaz.");
                if (phase.FrequencyDays is <= 0 || phase.PeriodInterval is <= 0)
                    Add(row, "period", "INVALID", "Sıklık ve periyot aralığı sıfırdan büyük olmalıdır.");
                if (!string.IsNullOrWhiteSpace(phase.PeriodType) && !Enum.TryParse<KkdPeriodType>(phase.PeriodType, true, out _))
                    Add(row, "periodType", "INVALID_ENUM", $"Geçersiz periyot tipi: {phase.PeriodType}");
            }
        }

        return new(issues.Count == 0, request.Rules.Count, request.Rules.Sum(x => x.Phases.Count),
            request.Rules.Count(x => x.StockId.HasValue), request.Rules.Count(x => !x.StockId.HasValue), issues);
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
        var validation = await ValidateMatrixAsync(id, request, ct);
        if (!validation.IsValid)
            throw AppException.BadRequest($"{validation.Issues[0].Message} Toplam {validation.Issues.Count} doğrulama hatası bulundu.");

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
        if (id.HasValue && !string.IsNullOrWhiteSpace(request.ExpectedRowVersion))
        {
            byte[] expected;
            try { expected = Convert.FromBase64String(request.ExpectedRowVersion); }
            catch (FormatException) { throw AppException.BadRequest("Matris sürüm bilgisi geçersiz."); }
            if (!entity.RowVersion.SequenceEqual(expected))
                throw AppException.Conflict("Hak matrisi başka bir kullanıcı tarafından değiştirildi. Güncel veriyi yükleyip tekrar deneyin.");
        }
        var now = DateTime.UtcNow;
        entity.CustomerId = request.CustomerId; entity.DepartmentId = request.DepartmentId; entity.RoleId = request.RoleId;
        entity.Code = code; entity.Name = RequiredText(request.Name, "Matris adı", 200);
        entity.EffectiveFrom = request.EffectiveFrom; entity.EffectiveTo = request.EffectiveTo;
        entity.IsActive = request.IsActive; entity.Description = Clean(request.Description, 1000);
        Touch(entity, actor, id.HasValue);
        if (!id.HasValue) await repository.AddAsync(entity, ct);

        var stockIds = request.Rules.Where(x => x.StockId.HasValue).Select(x => x.StockId!.Value).Distinct().ToArray();
        var stocks = await LoadStocksAsync(stockIds, ct);
        var existingRules = entity.Rules.Where(x => !x.IsDeleted)
            .ToDictionary(x => MatrixRuleKey(x.StockId, x.GroupCode), StringComparer.OrdinalIgnoreCase);
        var requestedRuleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in request.Rules)
        {
            stocks.TryGetValue(item.StockId ?? 0, out var stock);
            var groupCode = Normalize(item.GroupCode);
            var stockGroupCode = Normalize(stock?.GroupCode);
            if (stock is not null && groupCode.Length == 0)
                groupCode = stockGroupCode;
            if (groupCode.Length == 0)
                throw AppException.BadRequest("Stok seçilmediğinde hakediş grubu zorunludur.");
            var ruleKey = MatrixRuleKey(stock?.Id, groupCode);
            requestedRuleKeys.Add(ruleKey);
            var isNewRule = !existingRules.TryGetValue(ruleKey, out var rule);
            rule ??= new KkdEntitlementRule
            {
                BranchCode = entity.BranchCode,
                Matrix = entity, CreatedBy = actor, CreatedDate = now
            };
            rule.GroupCode = groupCode; rule.GroupName = Clean(item.GroupName, 200) ?? groupCode;
            rule.StockId = stock?.Id; rule.StockCodeSnapshot = stock?.ErpStockCode; rule.StockNameSnapshot = stock?.StockName;
            rule.StandardCode = Clean(item.StandardCode, 80); rule.StandardName = Clean(item.StandardName, 200);
            rule.AnnualIssueCount = item.AnnualIssueCount; rule.AnnualQuantity = item.AnnualQuantity;
            rule.MaxCarryQuantity = item.MaxCarryQuantity; rule.AllowBulkIssue = item.AllowBulkIssue;
            rule.IsMandatory = item.IsMandatory; rule.SortOrder = item.SortOrder; rule.IsActive = item.IsActive;
            rule.Description = Clean(item.Description, 1000);
            if (!isNewRule) Touch(rule, actor, true);

            var existingPhases = rule.Phases.Where(x => !x.IsDeleted)
                .ToDictionary(x => MatrixPhaseKey(x.PhaseType, x.OffsetMonths), StringComparer.OrdinalIgnoreCase);
            var requestedPhaseKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                var phaseKey = MatrixPhaseKey(phaseType, phaseRequest.OffsetMonths);
                requestedPhaseKeys.Add(phaseKey);
                var isNewPhase = !existingPhases.TryGetValue(phaseKey, out var phase);
                phase ??= new KkdEntitlementPhase
                {
                    BranchCode = entity.BranchCode,
                    Rule = rule, CreatedBy = actor, CreatedDate = now
                };
                phase.PhaseType = phaseType; phase.OffsetMonths = phaseRequest.OffsetMonths;
                phase.Quantity = phaseRequest.Quantity; phase.AllowBulkIssue = phaseRequest.AllowBulkIssue;
                phase.FrequencyDays = phaseRequest.FrequencyDays; phase.QuantityPerFrequency = phaseRequest.QuantityPerFrequency;
                phase.PeriodType = periodType; phase.PeriodInterval = phaseRequest.PeriodInterval;
                phase.SortOrder = phaseRequest.SortOrder; phase.IsActive = phaseRequest.IsActive;
                phase.Description = Clean(phaseRequest.Description, 1000);
                if (isNewPhase) rule.Phases.Add(phase); else Touch(phase, actor, true);
            }
            foreach (var removedPhase in existingPhases.Where(x => !requestedPhaseKeys.Contains(x.Key)).Select(x => x.Value))
            {
                removedPhase.IsDeleted = true; removedPhase.DeletedBy = actor; removedPhase.DeletedDate = now;
            }
            if (isNewRule) entity.Rules.Add(rule);
        }
        foreach (var removedRule in existingRules.Where(x => !requestedRuleKeys.Contains(x.Key)).Select(x => x.Value))
        {
            removedRule.IsDeleted = true; removedRule.DeletedBy = actor; removedRule.DeletedDate = now;
            foreach (var removedPhase in removedRule.Phases.Where(x => !x.IsDeleted))
            {
                removedPhase.IsDeleted = true; removedPhase.DeletedBy = actor; removedPhase.DeletedDate = now;
            }
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
        var groupCode = Normalize(request.GroupCode);
        if (groupCode.Length == 0) throw AppException.BadRequest("KKD grup kodu zorunludur.");
        if (request.RuleId.HasValue)
        {
            var ruleGroup = await uow.Repository<KkdEntitlementRule>().Query()
                .Where(x => x.Id == request.RuleId && x.IsActive)
                .Select(x => x.GroupCode)
                .SingleOrDefaultAsync(ct)
                ?? throw AppException.BadRequest("Aktif KKD kuralı bulunamadı.");
            if (!string.Equals(Normalize(ruleGroup), groupCode, StringComparison.Ordinal))
                throw AppException.BadRequest("Seçilen KKD kuralı ile grup kodu eşleşmiyor.");
        }
        var entity = new KkdEmployeeEntitlementOverride
        {
            EmployeeId = request.EmployeeId, RuleId = request.RuleId, GroupCode = groupCode,
            Quantity = request.Quantity, ValidFrom = request.ValidFrom, ValidTo = request.ValidTo,
            Reason = RequiredText(request.Reason, "Ek hak gerekçesi", 1000), ApprovedByUserId = actor,
            IsActive = request.IsActive, CreatedBy = actor, CreatedDate = DateTime.UtcNow
        };
        await uow.Repository<KkdEmployeeEntitlementOverride>().AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        return entity.Id;
    }

    public Task<PagedResponse<KkdOverrideRow>> GetOverridesPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var query = uow.Repository<KkdEmployeeEntitlementOverride>().Query()
            .Select(x => new KkdOverrideRow(
                x.Id, x.EmployeeId, x.Employee.EmployeeCode, x.Employee.FirstName + " " + x.Employee.LastName,
                x.RuleId, x.GroupCode, x.Quantity, x.ConsumedQuantity,
                x.Quantity > x.ConsumedQuantity ? x.Quantity - x.ConsumedQuantity : 0,
                x.ValidFrom, x.ValidTo, x.Reason, x.ApprovedByUserId, x.IsActive,
                x.CreatedDate, x.UpdatedDate, x.RowVersion))
            .ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["employeeCode"] = nameof(KkdOverrideRow.EmployeeCode),
                ["employeeName"] = nameof(KkdOverrideRow.EmployeeName),
                ["groupCode"] = nameof(KkdOverrideRow.GroupCode),
                ["reason"] = nameof(KkdOverrideRow.Reason)
            }, ["employeeCode", "employeeName", "groupCode", "reason"])
            .ApplySort(request, nameof(KkdOverrideRow.UpdatedDate));
        return query.ToPagedResponseAsync(request, ct);
    }

    public async Task<long> UpdateOverrideAsync(long id, KkdOverrideUpdateRequest request, long actor, CancellationToken ct = default)
    {
        if (request.Quantity <= 0) throw AppException.BadRequest("Ek hak miktarı sıfırdan büyük olmalıdır.");
        if (request.ValidTo.HasValue && request.ValidTo < request.ValidFrom)
            throw AppException.BadRequest("Ek hak tarih aralığı geçersiz.");
        var entity = await uow.Repository<KkdEmployeeEntitlementOverride>().FindByIdAsync(id, true, ct)
            ?? throw AppException.NotFound("Personel ek hakkı bulunamadı.");
        if (request.Quantity < entity.ConsumedQuantity)
            throw AppException.Conflict($"Ek hak miktarı tüketilmiş {entity.ConsumedQuantity:0.######} miktarın altına indirilemez.");
        byte[] expected;
        try { expected = Convert.FromBase64String(request.ExpectedRowVersion); }
        catch (FormatException) { throw AppException.BadRequest("Ek hak satır sürümü geçersiz."); }
        if (!entity.RowVersion.SequenceEqual(expected))
            throw AppException.Conflict("Ek hak başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyip tekrar deneyin.");

        var groupCode = Normalize(request.GroupCode);
        if (groupCode.Length == 0) throw AppException.BadRequest("KKD grup kodu zorunludur.");
        if (request.RuleId.HasValue)
        {
            var ruleGroup = await uow.Repository<KkdEntitlementRule>().Query()
                .Where(x => x.Id == request.RuleId && x.IsActive)
                .Select(x => x.GroupCode)
                .SingleOrDefaultAsync(ct)
                ?? throw AppException.BadRequest("Aktif KKD kuralı bulunamadı.");
            if (!string.Equals(Normalize(ruleGroup), groupCode, StringComparison.Ordinal))
                throw AppException.BadRequest("Seçilen KKD kuralı ile grup kodu eşleşmiyor.");
        }
        entity.RuleId = request.RuleId;
        entity.GroupCode = groupCode;
        entity.Quantity = request.Quantity;
        entity.ValidFrom = request.ValidFrom;
        entity.ValidTo = request.ValidTo;
        entity.Reason = RequiredText(request.Reason, "Ek hak gerekçesi", 1000);
        entity.IsActive = request.IsActive;
        entity.UpdatedBy = actor;
        entity.UpdatedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task DeleteOverrideAsync(long id, long actor, CancellationToken ct = default)
    {
        var entity = await uow.Repository<KkdEmployeeEntitlementOverride>().FindByIdAsync(id, true, ct)
            ?? throw AppException.NotFound("Personel ek hakkı bulunamadı.");
        if (entity.ConsumedQuantity > 0)
            throw AppException.Conflict("Tüketilmiş ek hak silinemez; geçmiş izlenebilirliği için pasife alınmalıdır.");
        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.DeletedBy = actor;
        entity.DeletedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<long, StockEntity>> LoadStocksAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var result = new Dictionary<long, StockEntity>();
        foreach (var chunk in ids.Chunk(1000))
        {
            var rows = await uow.Repository<StockEntity>().Query().Where(x => chunk.Contains(x.Id)).ToListAsync(ct);
            foreach (var row in rows) result[row.Id] = row;
        }
        return result;
    }

    private async Task<HashSet<string>> LoadStockGroupsAsync(IReadOnlyCollection<string> codes, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in codes.Chunk(1000))
        {
            var rows = await uow.Repository<StockEntity>().Query()
                .Where(x => x.GroupCode != null && chunk.Contains(x.GroupCode))
                .Select(x => x.GroupCode!).Distinct().ToListAsync(ct);
            result.UnionWith(rows);
        }
        return result;
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
    private static string MatrixRuleKey(long? stockId, string? groupCode) => $"{stockId?.ToString() ?? "GROUP"}|{Normalize(groupCode)}";
    private static string MatrixPhaseKey(KkdEntitlementPhaseType phaseType, int offsetMonths) => $"{phaseType}|{offsetMonths}";
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static void Touch(verii_wms_api_v2.Shared.Domain.BaseEntity entity, long actor, bool exists)
    {
        if (exists) { entity.UpdatedBy = actor; entity.UpdatedDate = DateTime.UtcNow; }
        else { entity.CreatedBy = actor; entity.CreatedDate = DateTime.UtcNow; }
    }
}
