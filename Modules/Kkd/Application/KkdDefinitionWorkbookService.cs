using System.Data;
using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed class KkdDefinitionWorkbookService(
    IUnitOfWork uow,
    IKkdDefinitionService definitions) : IKkdDefinitionWorkbookService
{
    public const int MaxRowsPerSheet = 10_000;
    public const int MaxImportFileSize = 15 * 1024 * 1024;
    private const int TemplateDataLastRow = 10_001;

    private const string GuideSheet = "00_KILAVUZ";
    private const string DepartmentSheet = "01_DEPARTMANLAR";
    private const string RoleSheet = "02_ROLLER";
    private const string EmployeeSheet = "03_PERSONELLER";
    private const string MatrixSheet = "04_HAK_MATRISLERI";
    private const string RuleSheet = "05_HAK_KURALLARI";
    private const string PhaseSheet = "06_HAK_DONEMLERI";

    private static readonly string[] DepartmentHeaders =
        ["ID", "Departman Kodu*", "Departman Adı*", "Aktif*"];
    private static readonly string[] RoleHeaders =
        ["ID", "Departman Kodu", "Rol Kodu*", "Rol Adı*", "Aktif*"];
    private static readonly string[] EmployeeHeaders =
    [
        "ID", "Personel Kodu*", "Ad*", "Soyad*", "Entegre Cari Kodu*", "Departman Kodu*",
        "Rol Kodu*", "QR Kodu*", "İşe Giriş Tarihi*", "Kullanıcı Adı / E-posta", "Aktif*"
    ];
    private static readonly string[] MatrixHeaders =
    [
        "ID", "Matris Kodu*", "Matris Adı*", "Entegre Cari Kodu*", "Departman Kodu*", "Rol Kodu*",
        "Başlangıç Tarihi", "Bitiş Tarihi", "Aktif*", "Açıklama"
    ];
    private static readonly string[] RuleHeaders =
    [
        "ID", "Matris Kodu*", "Stok Grup Kodu*", "Grup Adı", "Stok Kodu", "Standart Kodu",
        "Standart Adı", "Yıllık Veriliş Sayısı", "Yıllık Miktar", "Azami Devreden Miktar",
        "Toplu Verilebilir*", "Zorunlu*", "Sıra*", "Aktif*", "Açıklama"
    ];
    private static readonly string[] PhaseHeaders =
    [
        "ID", "Matris Kodu*", "Stok Kodu", "Stok Grup Kodu*", "Dönem Tipi*", "Başlangıç Ayı*",
        "Miktar*", "Toplu Verilebilir*", "Sıklık (Gün)", "Sıklık Başına Miktar", "Periyot Tipi",
        "Periyot Aralığı", "Sıra*", "Aktif*", "Açıklama"
    ];

    public async Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken ct = default)
    {
        using var branchScope = uow.BeginBranchScope(NormalizeBranch(branchCode));

        var departments = await uow.Repository<KkdDepartment>().Query()
            .OrderBy(x => x.Code).ToListAsync(ct);
        var roles = await uow.Repository<KkdRole>().Query()
            .Include(x => x.Department).OrderBy(x => x.Code).ToListAsync(ct);
        var customers = await uow.Repository<CustomerEntity>().Query()
            .OrderBy(x => x.CustomerCode)
            .Select(x => new CustomerReference(x.Id, x.CustomerCode, x.CustomerName))
            .ToListAsync(ct);
        var customerById = customers.ToDictionary(x => x.Id);
        var users = await uow.Repository<User>().Query()
            .OrderBy(x => x.Username)
            .Select(x => new UserReference(x.Id, x.Username, x.Email, x.IsActive))
            .ToListAsync(ct);
        var userById = users.ToDictionary(x => x.Id);
        var employees = await uow.Repository<KkdEmployee>().Query()
            .Include(x => x.Department).Include(x => x.Role)
            .OrderBy(x => x.EmployeeCode).ToListAsync(ct);
        var stocks = await uow.Repository<StockEntity>().Query()
            .OrderBy(x => x.ErpStockCode)
            .Select(x => new StockReference(x.Id, x.ErpStockCode, x.StockName, x.GroupCode, x.BaseUnitCode))
            .ToListAsync(ct);
        var matrices = await uow.Repository<KkdEntitlementMatrix>().Query()
            .AsSplitQuery()
            .Include(x => x.Department).Include(x => x.Role)
            .Include(x => x.Rules).ThenInclude(x => x.Phases)
            .OrderBy(x => x.Code).ToListAsync(ct);

        using var workbook = new XLWorkbook();
        CreateGuide(workbook);

        var departmentSheet = CreateDataSheet(workbook, DepartmentSheet, DepartmentHeaders);
        for (var index = 0; index < departments.Count; index++)
            WriteRow(departmentSheet, index + 2,
                departments[index].Id, departments[index].Code, departments[index].Name, YesNo(departments[index].IsActive));
        FinishDataSheet(departmentSheet, departments.Count, DepartmentHeaders.Length, [2]);

        var roleSheet = CreateDataSheet(workbook, RoleSheet, RoleHeaders);
        for (var index = 0; index < roles.Count; index++)
            WriteRow(roleSheet, index + 2,
                roles[index].Id, roles[index].Department?.Code, roles[index].Code, roles[index].Name, YesNo(roles[index].IsActive));
        FinishDataSheet(roleSheet, roles.Count, RoleHeaders.Length, [2, 3]);

        var employeeSheet = CreateDataSheet(workbook, EmployeeSheet, EmployeeHeaders);
        for (var index = 0; index < employees.Count; index++)
        {
            var employee = employees[index];
            customerById.TryGetValue(employee.CustomerId, out var customer);
            userById.TryGetValue(employee.UserId ?? 0, out var user);
            WriteRow(employeeSheet, index + 2,
                employee.Id, employee.EmployeeCode, employee.FirstName, employee.LastName,
                customer?.Code, employee.Department.Code, employee.Role.Code, employee.QrCode,
                employee.EmploymentStartDate.ToDateTime(TimeOnly.MinValue), user?.Username, YesNo(employee.IsActive));
        }
        FinishDataSheet(employeeSheet, employees.Count, EmployeeHeaders.Length, [2, 5, 6, 7, 8, 10]);
        employeeSheet.Column(9).Style.DateFormat.Format = "dd.MM.yyyy";

        var matrixSheet = CreateDataSheet(workbook, MatrixSheet, MatrixHeaders);
        for (var index = 0; index < matrices.Count; index++)
        {
            var matrix = matrices[index];
            customerById.TryGetValue(matrix.CustomerId, out var customer);
            WriteRow(matrixSheet, index + 2,
                matrix.Id, matrix.Code, matrix.Name, customer?.Code, matrix.Department.Code, matrix.Role.Code,
                matrix.EffectiveFrom?.ToDateTime(TimeOnly.MinValue), matrix.EffectiveTo?.ToDateTime(TimeOnly.MinValue),
                YesNo(matrix.IsActive), matrix.Description);
        }
        FinishDataSheet(matrixSheet, matrices.Count, MatrixHeaders.Length, [2, 4, 5, 6]);
        matrixSheet.Columns(7, 8).Style.DateFormat.Format = "dd.MM.yyyy";

        var ruleSheet = CreateDataSheet(workbook, RuleSheet, RuleHeaders);
        var ruleRow = 2;
        foreach (var matrix in matrices)
        foreach (var rule in matrix.Rules.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder))
        {
            WriteRow(ruleSheet, ruleRow++, rule.Id, matrix.Code, rule.GroupCode, rule.GroupName,
                rule.StockCodeSnapshot, rule.StandardCode, rule.StandardName, rule.AnnualIssueCount,
                rule.AnnualQuantity, rule.MaxCarryQuantity, YesNo(rule.AllowBulkIssue), YesNo(rule.IsMandatory),
                rule.SortOrder, YesNo(rule.IsActive), rule.Description);
        }
        FinishDataSheet(ruleSheet, ruleRow - 2, RuleHeaders.Length, [2, 3, 5, 6]);

        var phaseSheet = CreateDataSheet(workbook, PhaseSheet, PhaseHeaders);
        var phaseRow = 2;
        foreach (var matrix in matrices)
        foreach (var rule in matrix.Rules.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder))
        foreach (var phase in rule.Phases.Where(x => !x.IsDeleted).OrderBy(x => x.SortOrder))
        {
            WriteRow(phaseSheet, phaseRow++, phase.Id, matrix.Code, rule.StockCodeSnapshot, rule.GroupCode,
                PhaseTypeLabel(phase.PhaseType), phase.OffsetMonths, phase.Quantity, YesNo(phase.AllowBulkIssue),
                phase.FrequencyDays, phase.QuantityPerFrequency, PeriodTypeLabel(phase.PeriodType), phase.PeriodInterval,
                phase.SortOrder, YesNo(phase.IsActive), phase.Description);
        }
        FinishDataSheet(phaseSheet, phaseRow - 2, PhaseHeaders.Length, [2, 3, 4]);

        AddBooleanValidation(departmentSheet, 4);
        AddBooleanValidation(roleSheet, 5);
        AddBooleanValidation(employeeSheet, 11);
        AddBooleanValidation(matrixSheet, 9);
        foreach (var column in new[] { 11, 12, 14 }) AddBooleanValidation(ruleSheet, column);
        foreach (var column in new[] { 8, 14 }) AddBooleanValidation(phaseSheet, column);
        AddListValidation(phaseSheet.Range(2, 5, TemplateDataLastRow, 5), "İlk Hak,Ay Sonrası,Tekrarlayan");
        AddListValidation(phaseSheet.Range(2, 11, TemplateDataLastRow, 11), "Gün,Ay,Yıl");

        CreateReferenceSheet(workbook, "REF_CARILER", ["Entegre Cari Kodu", "Cari Adı"],
            customers.Select(x => new object?[] { x.Code, x.Name }));
        CreateReferenceSheet(workbook, "REF_STOKLAR", ["Stok Kodu", "Stok Adı", "Stok Grup Kodu", "Birim"],
            stocks.Select(x => new object?[] { x.Code, x.Name, x.GroupCode, x.UnitCode }));
        CreateReferenceSheet(workbook, "REF_KULLANICILAR", ["Kullanıcı Adı", "E-posta", "Aktif"],
            users.Select(x => new object?[] { x.Username, x.Email, YesNo(x.IsActive) }));

        workbook.Worksheet(GuideSheet).Position = 1;
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<KkdDefinitionWorkbookImportResult> ImportAsync(
        Stream workbookStream, string branchCode, long actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workbookStream);
        await using var buffered = await BufferAsync(workbookStream, ct);
        using var workbook = OpenWorkbook(buffered);

        var parseErrors = new List<string>();
        var departments = ParseDepartments(RequireSheet(workbook, DepartmentSheet), parseErrors);
        var roles = ParseRoles(RequireSheet(workbook, RoleSheet), parseErrors);
        var employees = ParseEmployees(RequireSheet(workbook, EmployeeSheet), parseErrors);
        var matrices = ParseMatrices(RequireSheet(workbook, MatrixSheet), parseErrors);
        var rules = ParseRules(RequireSheet(workbook, RuleSheet), parseErrors);
        var phases = ParsePhases(RequireSheet(workbook, PhaseSheet), parseErrors);
        var totalRows = departments.Count + roles.Count + employees.Count + matrices.Count + rules.Count + phases.Count;
        if (totalRows == 0) parseErrors.Add("Aktarılacak veri satırı bulunamadı.");
        ValidateWorkbookDuplicates(departments, roles, employees, matrices, rules, phases, parseErrors);
        ThrowIfErrors(parseErrors);

        using var branchScope = uow.BeginBranchScope(NormalizeBranch(branchCode));
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var departmentCounter = new Counter();
            var roleCounter = new Counter();
            var employeeCounter = new Counter();
            var matrixCounter = new Counter();

            var departmentEntities = await uow.Repository<KkdDepartment>().Query(true).ToListAsync(token);
            var departmentsById = departmentEntities.ToDictionary(x => x.Id);
            var departmentsByCode = departmentEntities.ToDictionary(x => Normalize(x.Code), StringComparer.OrdinalIgnoreCase);
            foreach (var row in departments)
            {
                var existing = ResolveExisting(row.Id, row.Code, departmentsById, departmentsByCode, DepartmentSheet, row.Row);
                var request = new KkdDepartmentUpsertRequest(row.Code, row.Name, row.IsActive);
                if (existing is not null && SameDepartment(existing, request)) { departmentCounter.Unchanged++; continue; }
                var oldCode = existing?.Code;
                var id = await ExecuteRow(DepartmentSheet, row.Row,
                    () => definitions.UpsertDepartmentAsync(existing?.Id, request, actor, token));
                var saved = await uow.Repository<KkdDepartment>().FindByIdAsync(id, true, token)
                    ?? throw AppException.BadRequest($"{DepartmentSheet} {row.Row}. satır kaydedilemedi.");
                if (!string.IsNullOrWhiteSpace(oldCode)) departmentsByCode.Remove(Normalize(oldCode));
                departmentsById[id] = saved;
                departmentsByCode[Normalize(saved.Code)] = saved;
                if (existing is null) departmentCounter.Created++; else departmentCounter.Updated++;
            }

            var roleEntities = await uow.Repository<KkdRole>().Query(true).ToListAsync(token);
            var rolesById = roleEntities.ToDictionary(x => x.Id);
            var rolesByKey = roleEntities.ToDictionary(x => RoleKey(x.DepartmentId, x.Code), StringComparer.OrdinalIgnoreCase);
            foreach (var row in roles)
            {
                var departmentId = ResolveOptionalDepartment(row.DepartmentCode, departmentsByCode, RoleSheet, row.Row);
                KkdRole? existing = null;
                if (row.Id.HasValue)
                    existing = rolesById.TryGetValue(row.Id.Value, out var byId) ? byId
                        : throw RowError(RoleSheet, row.Row, $"{row.Id} ID'li rol bulunamadı.");
                else
                    rolesByKey.TryGetValue(RoleKey(departmentId, row.Code), out existing);
                var request = new KkdRoleUpsertRequest(departmentId, row.Code, row.Name, row.IsActive);
                if (existing is not null && SameRole(existing, request)) { roleCounter.Unchanged++; continue; }
                var oldKey = existing is null ? null : RoleKey(existing.DepartmentId, existing.Code);
                var id = await ExecuteRow(RoleSheet, row.Row,
                    () => definitions.UpsertRoleAsync(existing?.Id, request, actor, token));
                var saved = await uow.Repository<KkdRole>().FindByIdAsync(id, true, token)
                    ?? throw RowError(RoleSheet, row.Row, "Rol kaydedilemedi.");
                if (oldKey is not null) rolesByKey.Remove(oldKey);
                rolesById[id] = saved;
                rolesByKey[RoleKey(saved.DepartmentId, saved.Code)] = saved;
                if (existing is null) roleCounter.Created++; else roleCounter.Updated++;
            }

            var customers = await uow.Repository<CustomerEntity>().Query()
                .Select(x => new CustomerReference(x.Id, x.CustomerCode, x.CustomerName)).ToListAsync(token);
            var customersByCode = customers.ToDictionary(x => Normalize(x.Code), StringComparer.OrdinalIgnoreCase);
            var customersById = customers.ToDictionary(x => x.Id);
            var users = await uow.Repository<User>().Query()
                .Select(x => new UserReference(x.Id, x.Username, x.Email, x.IsActive)).ToListAsync(token);
            var usersByLogin = users.SelectMany(x => new[] { (Key: Normalize(x.Username), Value: x), (Key: Normalize(x.Email), Value: x) })
                .Where(x => x.Key.Length > 0).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);
            var employeeEntities = await uow.Repository<KkdEmployee>().Query(true).ToListAsync(token);
            var employeesById = employeeEntities.ToDictionary(x => x.Id);
            var employeesByCode = employeeEntities.ToDictionary(x => Normalize(x.EmployeeCode), StringComparer.OrdinalIgnoreCase);
            foreach (var row in employees)
            {
                var customer = ResolveRequired(customersByCode, row.CustomerCode, EmployeeSheet, row.Row, "Entegre cari");
                var department = ResolveRequired(departmentsByCode, row.DepartmentCode, EmployeeSheet, row.Row, "Departman");
                if (!rolesByKey.TryGetValue(RoleKey(department.Id, row.RoleCode), out var role))
                    throw RowError(EmployeeSheet, row.Row, $"'{row.RoleCode}' rolü '{department.Code}' departmanında bulunamadı.");
                UserReference? user = null;
                if (!string.IsNullOrWhiteSpace(row.UserLogin) && !usersByLogin.TryGetValue(Normalize(row.UserLogin), out user))
                    throw RowError(EmployeeSheet, row.Row, $"'{row.UserLogin}' kullanıcı adı/e-posta bulunamadı.");
                var existing = ResolveExisting(row.Id, row.EmployeeCode, employeesById, employeesByCode, EmployeeSheet, row.Row);
                var request = new KkdEmployeeUpsertRequest(customer.Id, user?.Id, row.EmployeeCode, row.FirstName, row.LastName,
                    department.Id, role.Id, row.QrCode, row.EmploymentStartDate, row.IsActive);
                if (existing is not null && SameEmployee(existing, request)) { employeeCounter.Unchanged++; continue; }
                var oldCode = existing?.EmployeeCode;
                var id = await ExecuteRow(EmployeeSheet, row.Row,
                    () => definitions.UpsertEmployeeAsync(existing?.Id, request, actor, token));
                var saved = await uow.Repository<KkdEmployee>().FindByIdAsync(id, true, token)
                    ?? throw RowError(EmployeeSheet, row.Row, "Personel kaydedilemedi.");
                if (!string.IsNullOrWhiteSpace(oldCode)) employeesByCode.Remove(Normalize(oldCode));
                employeesById[id] = saved;
                employeesByCode[Normalize(saved.EmployeeCode)] = saved;
                if (existing is null) employeeCounter.Created++; else employeeCounter.Updated++;
            }

            var stocks = await uow.Repository<StockEntity>().Query()
                .Select(x => new StockReference(x.Id, x.ErpStockCode, x.StockName, x.GroupCode, x.BaseUnitCode))
                .ToListAsync(token);
            var stocksByCode = stocks.ToDictionary(x => Normalize(x.Code), StringComparer.OrdinalIgnoreCase);
            var matrixEntities = await uow.Repository<KkdEntitlementMatrix>().Query(true).ToListAsync(token);
            var matricesById = matrixEntities.ToDictionary(x => x.Id);
            var matricesByCode = matrixEntities.ToDictionary(x => Normalize(x.Code), StringComparer.OrdinalIgnoreCase);
            var matrixRowsByCode = matrices.ToDictionary(x => Normalize(x.Code), StringComparer.OrdinalIgnoreCase);
            var rulesByMatrix = rules.GroupBy(x => Normalize(x.MatrixCode), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            var phasesByMatrix = phases.GroupBy(x => Normalize(x.MatrixCode), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            var targetCodes = matrixRowsByCode.Keys.Concat(rulesByMatrix.Keys).Concat(phasesByMatrix.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();

            foreach (var targetCode in targetCodes)
            {
                matrixRowsByCode.TryGetValue(targetCode, out var matrixRow);
                KkdEntitlementMatrix? existingEntity = null;
                if (matrixRow?.Id is not null)
                    existingEntity = matricesById.TryGetValue(matrixRow.Id.Value, out var byId) ? byId
                        : throw RowError(MatrixSheet, matrixRow.Row, $"{matrixRow.Id} ID'li hak matrisi bulunamadı.");
                else
                    matricesByCode.TryGetValue(targetCode, out existingEntity);
                if (matrixRow is null && existingEntity is null)
                    throw AppException.BadRequest($"{RuleSheet}/{PhaseSheet}: '{targetCode}' matris kodu {MatrixSheet} sayfasında veya sistemde bulunamadı.");

                var detail = existingEntity is null ? null : await definitions.GetMatrixAsync(existingEntity.Id, token);
                MatrixRow baseRow;
                if (matrixRow is not null) baseRow = matrixRow;
                else
                {
                    if (!customersById.TryGetValue(detail!.CustomerId, out var existingCustomer)
                        || !departmentsById.TryGetValue(detail.DepartmentId, out var existingDepartment)
                        || !rolesById.TryGetValue(detail.RoleId, out var existingRole))
                        throw AppException.BadRequest($"'{targetCode}' matrisinin cari/departman/rol bağlantısı çözümlenemedi.");
                    baseRow = new MatrixRow(0, detail.Id, detail.Code, detail.Name, existingCustomer.Code,
                        existingDepartment.Code, existingRole.Code, detail.EffectiveFrom, detail.EffectiveTo,
                        detail.IsActive, detail.Description);
                }
                var customer = ResolveRequired(customersByCode, baseRow.CustomerCode, MatrixSheet, baseRow.Row, "Entegre cari");
                var department = ResolveRequired(departmentsByCode, baseRow.DepartmentCode, MatrixSheet, baseRow.Row, "Departman");
                if (!rolesByKey.TryGetValue(RoleKey(department.Id, baseRow.RoleCode), out var role))
                    throw RowError(MatrixSheet, baseRow.Row, $"'{baseRow.RoleCode}' rolü '{department.Code}' departmanında bulunamadı.");

                rulesByMatrix.TryGetValue(targetCode, out var importedRules);
                phasesByMatrix.TryGetValue(targetCode, out var importedPhases);
                var mergedRules = BuildMergedRules(targetCode, importedRules ?? [], importedPhases ?? [], detail, stocksByCode);
                if (mergedRules.Count == 0)
                    throw RowError(MatrixSheet, baseRow.Row, "Yeni hak matrisi en az bir hak kuralı ve dönem içermelidir.");
                var request = new KkdMatrixUpsertRequest(customer.Id, department.Id, role.Id, baseRow.Code, baseRow.Name,
                    baseRow.EffectiveFrom, baseRow.EffectiveTo, baseRow.IsActive, baseRow.Description, mergedRules,
                    detail is null ? null : Convert.ToBase64String(detail.RowVersion));
                if (detail is not null && MatrixEquivalent(detail, request)) { matrixCounter.Unchanged++; continue; }
                var id = await ExecuteRow(MatrixSheet, baseRow.Row,
                    () => definitions.UpsertMatrixAsync(existingEntity?.Id, request, actor, token));
                var saved = await uow.Repository<KkdEntitlementMatrix>().FindByIdAsync(id, true, token)
                    ?? throw RowError(MatrixSheet, baseRow.Row, "Hak matrisi kaydedilemedi.");
                if (existingEntity is not null) matricesByCode.Remove(Normalize(existingEntity.Code));
                matricesById[id] = saved;
                matricesByCode[Normalize(saved.Code)] = saved;
                if (existingEntity is null) matrixCounter.Created++; else matrixCounter.Updated++;
            }

            return BuildResult(totalRows, departmentCounter, roleCounter, employeeCounter, matrixCounter);
        }, ct, IsolationLevel.ReadCommitted);
    }

    private static IReadOnlyList<KkdRuleUpsertRequest> BuildMergedRules(
        string matrixCode,
        IReadOnlyList<RuleRow> importedRules,
        IReadOnlyList<PhaseRow> importedPhases,
        KkdMatrixDetail? existing,
        IReadOnlyDictionary<string, StockReference> stocksByCode)
    {
        var existingRules = existing?.Rules.ToList() ?? [];
        var existingById = existingRules.ToDictionary(x => x.Id);
        var existingByKey = existingRules.ToDictionary(x => RuleKey(x.StockCode, x.GroupCode), StringComparer.OrdinalIgnoreCase);
        var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<KkdRuleUpsertRequest>();

        foreach (var row in importedRules.OrderBy(x => x.SortOrder).ThenBy(x => x.Row))
        {
            var key = RuleKey(row.StockCode, row.GroupCode);
            if (!importedKeys.Add(key)) throw RowError(RuleSheet, row.Row, $"'{matrixCode}' matrisi içinde aynı stok/grup kuralı tekrarlandı.");
            KkdRuleDetail? current = null;
            if (row.Id.HasValue)
            {
                if (!existingById.TryGetValue(row.Id.Value, out current))
                    throw RowError(RuleSheet, row.Row, $"{row.Id} ID'li kural bu matriste bulunamadı.");
            }
            else existingByKey.TryGetValue(key, out current);

            StockReference? stock = null;
            if (!string.IsNullOrWhiteSpace(row.StockCode) && !stocksByCode.TryGetValue(Normalize(row.StockCode), out stock))
                throw RowError(RuleSheet, row.Row, $"'{row.StockCode}' stok kodu bulunamadı.");
            var groupCode = Normalize(row.GroupCode);
            if (groupCode.Length == 0) groupCode = Normalize(stock?.GroupCode);
            if (groupCode.Length == 0) throw RowError(RuleSheet, row.Row, "Stok seçilmediğinde stok grup kodu zorunludur.");

            var phaseRows = importedPhases.Where(x => RuleKey(x.StockCode, x.GroupCode) == key).ToList();
            var phases = MergePhases(phaseRows, current?.Phases ?? [], row);
            result.Add(new(groupCode, NullIfEmpty(row.GroupName), stock?.Id, NullIfEmpty(row.StandardCode),
                NullIfEmpty(row.StandardName), row.AnnualIssueCount, row.AnnualQuantity, row.MaxCarryQuantity,
                row.AllowBulkIssue, row.IsMandatory, row.SortOrder, row.IsActive, NullIfEmpty(row.Description), phases));
        }

        foreach (var current in existingRules.Where(x => !importedKeys.Contains(RuleKey(x.StockCode, x.GroupCode))))
            result.Add(ToRequest(current));

        var orphanPhase = importedPhases.FirstOrDefault(x => !importedKeys.Contains(RuleKey(x.StockCode, x.GroupCode)));
        if (orphanPhase is not null)
            throw RowError(PhaseSheet, orphanPhase.Row, "Dönemin bağlı olduğu hak kuralı bulunamadı.");
        return result.OrderBy(x => x.SortOrder).ToArray();
    }

    private static IReadOnlyList<KkdPhaseUpsertRequest> MergePhases(
        IReadOnlyList<PhaseRow> imported,
        IReadOnlyList<KkdPhaseDetail> existing,
        RuleRow rule)
    {
        if (imported.Count == 0)
        {
            if (existing.Count > 0) return existing.Select(ToRequest).ToArray();
            throw RowError(RuleSheet, rule.Row, "Yeni hak kuralı için en az bir dönem satırı girilmelidir.");
        }
        var existingById = existing.ToDictionary(x => x.Id);
        var existingByKey = existing.ToDictionary(x => PhaseKey(x.PhaseType, x.OffsetMonths), StringComparer.OrdinalIgnoreCase);
        var matchedExisting = new HashSet<long>();
        var result = new List<KkdPhaseUpsertRequest>();
        foreach (var row in imported.OrderBy(x => x.SortOrder).ThenBy(x => x.Row))
        {
            KkdPhaseDetail? current = null;
            if (row.Id.HasValue)
            {
                if (!existingById.TryGetValue(row.Id.Value, out current))
                    throw RowError(PhaseSheet, row.Row, $"{row.Id} ID'li dönem bu kuralda bulunamadı.");
            }
            else existingByKey.TryGetValue(PhaseKey(row.PhaseType, row.OffsetMonths), out current);
            if (current is not null) matchedExisting.Add(current.Id);
            result.Add(new(row.PhaseType, row.OffsetMonths, row.Quantity, row.AllowBulkIssue,
                row.FrequencyDays, row.QuantityPerFrequency, row.PeriodType, row.PeriodInterval,
                row.SortOrder, row.IsActive, NullIfEmpty(row.Description)));
        }
        foreach (var current in existing.Where(x => !matchedExisting.Contains(x.Id))) result.Add(ToRequest(current));
        return result.OrderBy(x => x.SortOrder).ToArray();
    }

    private static KkdRuleUpsertRequest ToRequest(KkdRuleDetail x) =>
        new(x.GroupCode, x.GroupName, x.StockId, x.StandardCode, x.StandardName, x.AnnualIssueCount,
            x.AnnualQuantity, x.MaxCarryQuantity, x.AllowBulkIssue, x.IsMandatory, x.SortOrder,
            x.IsActive, x.Description, x.Phases.Select(ToRequest).ToArray());

    private static KkdPhaseUpsertRequest ToRequest(KkdPhaseDetail x) =>
        new(x.PhaseType, x.OffsetMonths, x.Quantity, x.AllowBulkIssue, x.FrequencyDays,
            x.QuantityPerFrequency, x.PeriodType, x.PeriodInterval, x.SortOrder, x.IsActive, x.Description);

    private static bool MatrixEquivalent(KkdMatrixDetail current, KkdMatrixUpsertRequest requested)
    {
        var currentRequest = new KkdMatrixUpsertRequest(current.CustomerId, current.DepartmentId, current.RoleId,
            current.Code, current.Name, current.EffectiveFrom, current.EffectiveTo, current.IsActive,
            current.Description, current.Rules.Select(ToRequest).ToArray());
        return CanonicalMatrix(currentRequest) == CanonicalMatrix(requested);
    }

    private static string CanonicalMatrix(KkdMatrixUpsertRequest request) => JsonSerializer.Serialize(new
    {
        request.CustomerId,
        request.DepartmentId,
        request.RoleId,
        Code = Normalize(request.Code),
        Name = request.Name.Trim(),
        request.EffectiveFrom,
        request.EffectiveTo,
        request.IsActive,
        Description = NullIfEmpty(request.Description),
        Rules = request.Rules.OrderBy(x => x.SortOrder).ThenBy(x => x.StockId).ThenBy(x => x.GroupCode).Select(x => new
        {
            GroupCode = Normalize(x.GroupCode), GroupName = NullIfEmpty(x.GroupName), x.StockId,
            StandardCode = NullIfEmpty(x.StandardCode), StandardName = NullIfEmpty(x.StandardName),
            x.AnnualIssueCount, x.AnnualQuantity, x.MaxCarryQuantity, x.AllowBulkIssue, x.IsMandatory,
            x.SortOrder, x.IsActive, Description = NullIfEmpty(x.Description),
            Phases = x.Phases.OrderBy(p => p.SortOrder).ThenBy(p => p.PhaseType).ThenBy(p => p.OffsetMonths).Select(p => new
            {
                PhaseType = NormalizePhaseType(p.PhaseType), p.OffsetMonths, p.Quantity, p.AllowBulkIssue,
                p.FrequencyDays, p.QuantityPerFrequency, PeriodType = NormalizePeriodType(p.PeriodType),
                p.PeriodInterval, p.SortOrder, p.IsActive, Description = NullIfEmpty(p.Description)
            })
        })
    });

    private static KkdDefinitionWorkbookImportResult BuildResult(
        int totalRows, Counter departments, Counter roles, Counter employees, Counter matrices)
    {
        var created = departments.Created + roles.Created + employees.Created + matrices.Created;
        var updated = departments.Updated + roles.Updated + employees.Updated + matrices.Updated;
        var unchanged = departments.Unchanged + roles.Unchanged + employees.Unchanged + matrices.Unchanged;
        return new(totalRows, created, updated, unchanged,
            departments.ToResult(), roles.ToResult(), employees.ToResult(), matrices.ToResult(),
            ["Excel'de bulunmayan mevcut kayıtlar silinmedi. Pasife alma yalnız Aktif = HAYIR ile yapılır."]);
    }

    private static bool SameDepartment(KkdDepartment x, KkdDepartmentUpsertRequest r) =>
        Normalize(x.Code) == Normalize(r.Code) && x.Name.Trim() == r.Name.Trim() && x.IsActive == r.IsActive;
    private static bool SameRole(KkdRole x, KkdRoleUpsertRequest r) =>
        x.DepartmentId == r.DepartmentId && Normalize(x.Code) == Normalize(r.Code)
        && x.Name.Trim() == r.Name.Trim() && x.IsActive == r.IsActive;
    private static bool SameEmployee(KkdEmployee x, KkdEmployeeUpsertRequest r) =>
        x.CustomerId == r.CustomerId && x.UserId == r.UserId && Normalize(x.EmployeeCode) == Normalize(r.EmployeeCode)
        && x.FirstName.Trim() == r.FirstName.Trim() && x.LastName.Trim() == r.LastName.Trim()
        && x.DepartmentId == r.DepartmentId && x.RoleId == r.RoleId && Normalize(x.QrCode) == Normalize(r.QrCode)
        && x.EmploymentStartDate == r.EmploymentStartDate && x.IsActive == r.IsActive;

    private static T? ResolveExisting<T>(long? id, string code, IReadOnlyDictionary<long, T> byId,
        IReadOnlyDictionary<string, T> byCode, string sheet, int row)
    {
        if (id.HasValue)
            return byId.TryGetValue(id.Value, out var byIdentifier) ? byIdentifier
                : throw RowError(sheet, row, $"{id} ID'li kayıt bulunamadı.");
        byCode.TryGetValue(Normalize(code), out var byNaturalKey);
        return byNaturalKey;
    }

    private static long? ResolveOptionalDepartment(string? code, IReadOnlyDictionary<string, KkdDepartment> departments,
        string sheet, int row)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return ResolveRequired(departments, code, sheet, row, "Departman").Id;
    }

    private static T ResolveRequired<T>(IReadOnlyDictionary<string, T> dictionary, string code,
        string sheet, int row, string label) =>
        dictionary.TryGetValue(Normalize(code), out var value) ? value
            : throw RowError(sheet, row, $"'{code}' {label.ToLowerInvariant()} kodu bulunamadı.");

    private static async Task<T> ExecuteRow<T>(string sheet, int row, Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        { throw RowError(sheet, row, exception.Message); }
    }

    private static AppException RowError(string sheet, int row, string message) =>
        AppException.BadRequest($"{sheet} sayfası, {row}. satır: {message}");

    private static void ValidateWorkbookDuplicates(
        IReadOnlyList<DepartmentRow> departments, IReadOnlyList<RoleRow> roles, IReadOnlyList<EmployeeRow> employees,
        IReadOnlyList<MatrixRow> matrices, IReadOnlyList<RuleRow> rules, IReadOnlyList<PhaseRow> phases,
        ICollection<string> errors)
    {
        AddDuplicateErrors(departments, x => Normalize(x.Code), x => x.Row, DepartmentSheet, "Departman Kodu", errors);
        AddDuplicateErrors(roles, x => $"{Normalize(x.DepartmentCode)}|{Normalize(x.Code)}", x => x.Row, RoleSheet, "Departman + Rol Kodu", errors);
        AddDuplicateErrors(employees, x => Normalize(x.EmployeeCode), x => x.Row, EmployeeSheet, "Personel Kodu", errors);
        AddDuplicateErrors(employees, x => Normalize(x.QrCode), x => x.Row, EmployeeSheet, "QR Kodu", errors);
        AddDuplicateErrors(matrices, x => Normalize(x.Code), x => x.Row, MatrixSheet, "Matris Kodu", errors);
        AddDuplicateErrors(rules, x => $"{Normalize(x.MatrixCode)}|{RuleKey(x.StockCode, x.GroupCode)}", x => x.Row, RuleSheet, "Matris + Stok/Grup", errors);
        AddDuplicateErrors(phases,
            x => $"{Normalize(x.MatrixCode)}|{RuleKey(x.StockCode, x.GroupCode)}|{NormalizePhaseType(x.PhaseType)}|{x.OffsetMonths}",
            x => x.Row, PhaseSheet, "Matris + Kural + Dönem Tipi + Başlangıç Ayı", errors);
    }

    private static void AddDuplicateErrors<T>(IEnumerable<T> rows, Func<T, string> key, Func<T, int> row,
        string sheet, string field, ICollection<string> errors)
    {
        foreach (var duplicate in rows.GroupBy(key, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            errors.Add($"{sheet}: {field} tekrarlı. Satırlar: {string.Join(", ", duplicate.Select(row))}.");
    }

    private static List<DepartmentRow> ParseDepartments(IXLWorksheet sheet, ICollection<string> errors) =>
        ParseRows(sheet, DepartmentHeaders, errors, row => new DepartmentRow(row.RowNumber(), OptionalLong(row, 1),
            Required(row, 2, "Departman Kodu"), Required(row, 3, "Departman Adı"), Boolean(row, 4, "Aktif")));

    private static List<RoleRow> ParseRoles(IXLWorksheet sheet, ICollection<string> errors) =>
        ParseRows(sheet, RoleHeaders, errors, row => new RoleRow(row.RowNumber(), OptionalLong(row, 1),
            CellText(row, 2), Required(row, 3, "Rol Kodu"), Required(row, 4, "Rol Adı"), Boolean(row, 5, "Aktif")));

    private static List<EmployeeRow> ParseEmployees(IXLWorksheet sheet, ICollection<string> errors) =>
        ParseRows(sheet, EmployeeHeaders, errors, row => new EmployeeRow(row.RowNumber(), OptionalLong(row, 1),
            Required(row, 2, "Personel Kodu"), Required(row, 3, "Ad"), Required(row, 4, "Soyad"),
            Required(row, 5, "Entegre Cari Kodu"), Required(row, 6, "Departman Kodu"), Required(row, 7, "Rol Kodu"),
            Required(row, 8, "QR Kodu"), Date(row, 9, "İşe Giriş Tarihi", false)!.Value, CellText(row, 10),
            Boolean(row, 11, "Aktif")));

    private static List<MatrixRow> ParseMatrices(IXLWorksheet sheet, ICollection<string> errors) =>
        ParseRows(sheet, MatrixHeaders, errors, row => new MatrixRow(row.RowNumber(), OptionalLong(row, 1),
            Required(row, 2, "Matris Kodu"), Required(row, 3, "Matris Adı"), Required(row, 4, "Entegre Cari Kodu"),
            Required(row, 5, "Departman Kodu"), Required(row, 6, "Rol Kodu"), Date(row, 7, "Başlangıç Tarihi", true),
            Date(row, 8, "Bitiş Tarihi", true), Boolean(row, 9, "Aktif"), NullIfEmpty(CellText(row, 10))));

    private static List<RuleRow> ParseRules(IXLWorksheet sheet, ICollection<string> errors) =>
        ParseRows(sheet, RuleHeaders, errors, row => new RuleRow(row.RowNumber(), OptionalLong(row, 1),
            Required(row, 2, "Matris Kodu"), Required(row, 3, "Stok Grup Kodu"), CellText(row, 4), CellText(row, 5),
            CellText(row, 6), CellText(row, 7), OptionalInt(row, 8, "Yıllık Veriliş Sayısı"),
            OptionalDecimal(row, 9, "Yıllık Miktar"), OptionalDecimal(row, 10, "Azami Devreden Miktar"),
            Boolean(row, 11, "Toplu Verilebilir"), Boolean(row, 12, "Zorunlu"), Integer(row, 13, "Sıra"),
            Boolean(row, 14, "Aktif"), CellText(row, 15)));

    private static List<PhaseRow> ParsePhases(IXLWorksheet sheet, ICollection<string> errors) =>
        ParseRows(sheet, PhaseHeaders, errors, row => new PhaseRow(row.RowNumber(), OptionalLong(row, 1),
            Required(row, 2, "Matris Kodu"), CellText(row, 3), Required(row, 4, "Stok Grup Kodu"),
            ParsePhaseType(Required(row, 5, "Dönem Tipi")), Integer(row, 6, "Başlangıç Ayı"),
            Decimal(row, 7, "Miktar"), Boolean(row, 8, "Toplu Verilebilir"), OptionalInt(row, 9, "Sıklık"),
            OptionalDecimal(row, 10, "Sıklık Başına Miktar"), ParsePeriodType(CellText(row, 11)),
            OptionalInt(row, 12, "Periyot Aralığı"), Integer(row, 13, "Sıra"), Boolean(row, 14, "Aktif"),
            CellText(row, 15)));

    private static List<T> ParseRows<T>(IXLWorksheet sheet, IReadOnlyList<string> headers,
        ICollection<string> errors, Func<IXLRow, T> parser)
    {
        ValidateHeaders(sheet, headers);
        var rows = sheet.RowsUsed().Where(x => x.RowNumber() > 1 && HasData(x, headers.Count))
            .Take(MaxRowsPerSheet + 1).ToList();
        if (rows.Count > MaxRowsPerSheet)
            throw AppException.BadRequest($"{sheet.Name} sayfası en fazla {MaxRowsPerSheet:N0} veri satırı içerebilir.");
        var result = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            try { result.Add(parser(row)); }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            { errors.Add($"{sheet.Name} sayfası, {row.RowNumber()}. satır: {exception.Message}"); }
        }
        return result;
    }

    private static void ThrowIfErrors(IReadOnlyCollection<string> errors)
    {
        if (errors.Count == 0) return;
        var shown = errors.Take(30).ToArray();
        var suffix = errors.Count > shown.Length ? $" Ayrıca {errors.Count - shown.Length} hata daha var." : string.Empty;
        throw AppException.BadRequest("Excel doğrulanamadı: " + string.Join(" | ", shown) + suffix);
    }

    private static string Required(IXLRow row, int column, string field)
    {
        var value = CellText(row, column);
        if (value.Length == 0) throw new FormatException($"{field} zorunludur.");
        return value;
    }

    private static long? OptionalLong(IXLRow row, int column)
    {
        var cell = row.Cell(column);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<long>(out var value) && value > 0) return value;
        throw new FormatException($"{column}. kolon ID değeri pozitif tam sayı olmalıdır.");
    }

    private static int Integer(IXLRow row, int column, string field)
    {
        if (row.Cell(column).TryGetValue<int>(out var value)) return value;
        if (int.TryParse(CellText(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
        throw new FormatException($"{field} tam sayı olmalıdır.");
    }

    private static int? OptionalInt(IXLRow row, int column, string field) =>
        string.IsNullOrWhiteSpace(CellText(row, column)) ? null : Integer(row, column, field);

    private static decimal Decimal(IXLRow row, int column, string field)
    {
        if (row.Cell(column).TryGetValue<decimal>(out var value)) return value;
        var text = CellText(row, column);
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out value)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return value;
        throw new FormatException($"{field} sayısal olmalıdır.");
    }

    private static decimal? OptionalDecimal(IXLRow row, int column, string field) =>
        string.IsNullOrWhiteSpace(CellText(row, column)) ? null : Decimal(row, column, field);

    private static DateOnly? Date(IXLRow row, int column, string field, bool optional)
    {
        var cell = row.Cell(column);
        if (cell.IsEmpty() || string.IsNullOrWhiteSpace(CellText(row, column)))
        {
            if (optional) return null;
            throw new FormatException($"{field} zorunludur.");
        }
        if (cell.TryGetValue<DateTime>(out var date)) return DateOnly.FromDateTime(date);
        var text = CellText(row, column);
        if (DateOnly.TryParse(text, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var parsed)
            || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return parsed;
        throw new FormatException($"{field} geçerli bir tarih olmalıdır.");
    }

    private static bool Boolean(IXLRow row, int column, string field) => CellText(row, column).Trim().ToLowerInvariant() switch
    {
        "evet" or "e" or "true" or "1" or "yes" => true,
        "hayır" or "hayir" or "h" or "false" or "0" or "no" => false,
        _ => throw new FormatException($"{field} EVET veya HAYIR olmalıdır.")
    };

    private static string ParsePhaseType(string value) => Normalize(value) switch
    {
        "INITIAL" or "ILK HAK" or "İLK HAK" => nameof(KkdEntitlementPhaseType.Initial),
        "AFTERMONTHS" or "AY SONRASI" => nameof(KkdEntitlementPhaseType.AfterMonths),
        "RECURRING" or "TEKRARLAYAN" => nameof(KkdEntitlementPhaseType.Recurring),
        _ => throw new FormatException("Dönem Tipi; İlk Hak, Ay Sonrası veya Tekrarlayan olmalıdır.")
    };

    private static string? ParsePeriodType(string value) => Normalize(value) switch
    {
        "" => null,
        "DAY" or "GUN" or "GÜN" => nameof(KkdPeriodType.Day),
        "MONTH" or "AY" => nameof(KkdPeriodType.Month),
        "YEAR" or "YIL" => nameof(KkdPeriodType.Year),
        _ => throw new FormatException("Periyot Tipi; Gün, Ay veya Yıl olmalıdır.")
    };

    private static string NormalizePhaseType(string value) => ParsePhaseType(value);
    private static string? NormalizePeriodType(string? value) => string.IsNullOrWhiteSpace(value) ? null : ParsePeriodType(value);
    private static string RuleKey(string? stockCode, string? groupCode) =>
        !string.IsNullOrWhiteSpace(stockCode) ? $"S:{Normalize(stockCode)}" : $"G:{Normalize(groupCode)}";
    private static string PhaseKey(string phaseType, int offsetMonths) => $"{NormalizePhaseType(phaseType)}|{offsetMonths}";
    private static string RoleKey(long? departmentId, string code) => $"{departmentId?.ToString() ?? "GLOBAL"}|{Normalize(code)}";

    private static IXLWorksheet RequireSheet(XLWorkbook workbook, string name) =>
        workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw AppException.BadRequest($"'{name}' çalışma sayfası bulunamadı. Lütfen sistemden yeni şablon indirin.");

    private static void ValidateHeaders(IXLWorksheet sheet, IReadOnlyList<string> expected)
    {
        var actual = Enumerable.Range(1, expected.Count).Select(x => sheet.Cell(1, x).GetString().Trim()).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw AppException.BadRequest($"{sheet.Name} başlıkları veya sırası değişmiş. Sistemden yeni şablon indirin.");
    }

    private static bool HasData(IXLRow row, int count) =>
        Enumerable.Range(1, count).Any(column => !row.Cell(column).IsEmpty() && CellText(row, column).Length > 0);
    private static string CellText(IXLRow row, int column) => row.Cell(column).GetFormattedString().Trim();
    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    private static string NormalizeBranch(string? value) => string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string YesNo(bool value) => value ? "EVET" : "HAYIR";
    private static string PhaseTypeLabel(KkdEntitlementPhaseType value) => value switch
    { KkdEntitlementPhaseType.Initial => "İlk Hak", KkdEntitlementPhaseType.AfterMonths => "Ay Sonrası", _ => "Tekrarlayan" };
    private static string? PeriodTypeLabel(KkdPeriodType? value) => value switch
    { KkdPeriodType.Day => "Gün", KkdPeriodType.Month => "Ay", KkdPeriodType.Year => "Yıl", _ => null };

    private static XLWorkbook OpenWorkbook(Stream stream)
    {
        try { return new XLWorkbook(stream); }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        { throw AppException.BadRequest("Dosya geçerli bir XLSX çalışma kitabı değil."); }
    }

    private static async Task<MemoryStream> BufferAsync(Stream source, CancellationToken ct)
    {
        var target = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            if (total > MaxImportFileSize)
            {
                await target.DisposeAsync();
                throw AppException.BadRequest("KKD Excel dosyası en fazla 15 MB olabilir.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total == 0)
        {
            await target.DisposeAsync();
            throw AppException.BadRequest("Yüklenecek KKD Excel dosyası boş olamaz.");
        }
        target.Position = 0;
        return target;
    }

    private static void CreateGuide(XLWorkbook workbook)
    {
        var sheet = workbook.Worksheets.Add(GuideSheet);
        sheet.Range("A1:F2").Merge();
        sheet.Cell("A1").Value = "WMS V2 — KKD Tanımları ve Hak Matrisi Toplu Yönetimi";
        sheet.Range("A1:F2").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(17)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        var notices = new[]
        {
            ("1. Önce indirin", "Şablon mevcut departman, rol, personel ve hak matrislerini dolu getirir. Her zaman sistemden yeni şablon indirin."),
            ("2. Aynı dosyada düzenleyin", "ID kolonlarını değiştirmeyin. Mevcut satırı düzenlerseniz kayıt güncellenir; ID boş yeni satır eklerseniz kayıt oluşturulur."),
            ("3. Silme yok", "Satırı Excel'den silmek sistemdeki kaydı silmez. Kaydı kullanımdan kaldırmak için Aktif alanını HAYIR yapın."),
            ("4. Tek işlem", "Bütün sayfalar önce doğrulanır. Bir satır bile hatalıysa hiçbir kayıt eklenmez veya güncellenmez."),
            ("5. İşlem sırası", "Departmanlar → Roller → Personeller → Hak Matrisleri → Hak Kuralları → Hak Dönemleri sırasıyla işlenir."),
            ("6. Kodla bağlantı", "Cari, departman, rol, stok ve matris ilişkileri ID yerine görünen kodlarla kurulur. Kodları referans sayfalarından kopyalayın."),
            ("7. Matris kuralı", "Stok Kodu doluysa kural o stoğa özeldir. Stok Kodu boşsa Stok Grup Kodu üzerinden gruptaki stoklara uygulanır."),
            ("8. Dönem kuralı", "Her yeni hak kuralının en az bir dönemi olmalıdır. Dönem satırında aynı Matris + Stok/Grup kodunu kullanın."),
            ("9. Toplu güvenlik", "Aynı dosyanın tekrar yüklenmesi mükerrer kayıt üretmez; değişmeyen satırlar atlanır."),
        };
        var row = 4;
        foreach (var (title, text) in notices)
        {
            sheet.Cell(row, 1).Value = title;
            sheet.Range(row, 2, row, 6).Merge();
            sheet.Cell(row, 2).Value = text;
            sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#0E7490"));
            sheet.Range(row, 1, row, 6).Style.Fill.SetBackgroundColor(row % 2 == 0 ? XLColor.FromHtml("#ECFEFF") : XLColor.White)
                .Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(XLColor.FromHtml("#C7DCE5"))
                .Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top);
            sheet.Row(row).Height = 38;
            row++;
        }
        row += 2;
        sheet.Cell(row, 1).Value = "Dönem Tipi";
        sheet.Cell(row, 2).Value = "Ne zaman kullanılır?";
        sheet.Cell(row, 3).Value = "Örnek";
        StyleHeader(sheet.Range(row, 1, row, 3));
        WriteRow(sheet, row + 1, "İlk Hak", "İşe girişte bir defalık hak", "İlk gün 1 çift ayakkabı");
        WriteRow(sheet, row + 2, "Ay Sonrası", "İşe girişten belirli ay sonra", "6 ay sonra 1 mont");
        WriteRow(sheet, row + 3, "Tekrarlayan", "Belirli periyotta yenilenen hak", "Her 12 ayda 2 eldiven");
        sheet.Columns(1, 6).Width = 24;
        sheet.Column(2).Width = 46;
        sheet.Columns(3, 6).Width = 28;
        sheet.SheetView.FreezeRows(2);
        sheet.TabColor = XLColor.FromHtml("#0E7490");
    }

    private static IXLWorksheet CreateDataSheet(XLWorkbook workbook, string name, IReadOnlyList<string> headers)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var index = 0; index < headers.Count; index++) sheet.Cell(1, index + 1).Value = headers[index];
        StyleHeader(sheet.Range(1, 1, 1, headers.Count));
        sheet.SheetView.FreezeRows(1);
        sheet.TabColor = XLColor.FromHtml("#0EA5E9");
        sheet.Row(1).Height = 30;
        sheet.Column(1).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#EEF2F6"));
        sheet.Column(1).Style.Font.SetFontColor(XLColor.FromHtml("#64748B"));
        sheet.Column(1).Width = 12;
        return sheet;
    }

    private static void FinishDataSheet(IXLWorksheet sheet, int dataCount, int columnCount, IReadOnlyList<int> textColumns)
    {
        var lastRow = Math.Max(2, dataCount + 1);
        sheet.Range(1, 1, lastRow, columnCount).SetAutoFilter();
        sheet.Range(2, 1, lastRow, columnCount).Style.Border.SetBottomBorder(XLBorderStyleValues.Hair)
            .Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));
        foreach (var column in textColumns) sheet.Column(column).Style.NumberFormat.Format = "@";
        sheet.Columns(2, columnCount).AdjustToContents(1, Math.Min(lastRow, 1000), 12, 38);
        sheet.Columns(2, columnCount).Width = Math.Max(16, sheet.Column(2).Width);
    }

    private static void CreateReferenceSheet(XLWorkbook workbook, string name, IReadOnlyList<string> headers,
        IEnumerable<object?[]> rows)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var index = 0; index < headers.Count; index++) sheet.Cell(1, index + 1).Value = headers[index];
        StyleHeader(sheet.Range(1, 1, 1, headers.Count), "#475569");
        var rowNumber = 2;
        foreach (var row in rows) WriteRow(sheet, rowNumber++, row);
        var lastRow = Math.Max(2, rowNumber - 1);
        sheet.Range(1, 1, lastRow, headers.Count).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, headers.Count).AdjustToContents(1, Math.Min(lastRow, 5000), 12, 42);
        sheet.TabColor = XLColor.FromHtml("#94A3B8");
        sheet.Protect();
    }

    private static void StyleHeader(IXLRange range, string color = "#10243E") => range.Style
        .Fill.SetBackgroundColor(XLColor.FromHtml(color)).Font.SetFontColor(XLColor.White).Font.SetBold()
        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center)
        .Alignment.SetWrapText();

    private static void WriteRow(IXLWorksheet sheet, int row, params object?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var cell = sheet.Cell(row, index + 1);
            switch (values[index])
            {
                case null: break;
                case string value: cell.Value = value; break;
                case long value: cell.Value = value; break;
                case int value: cell.Value = value; break;
                case decimal value: cell.Value = value; break;
                case DateTime value: cell.Value = value; break;
                default: cell.Value = values[index]?.ToString() ?? string.Empty; break;
            }
        }
    }

    private static void AddBooleanValidation(IXLWorksheet sheet, int column) =>
        AddListValidation(sheet.Range(2, column, TemplateDataLastRow, column), "EVET,HAYIR");

    private static void AddListValidation(IXLRange range, string values)
    {
        var validation = range.CreateDataValidation();
        validation.List($"\"{values}\"", true);
        validation.IgnoreBlanks = false;
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Stop;
        validation.ErrorTitle = "Geçersiz değer";
        validation.ErrorMessage = "Hücre için tanımlı listeden bir değer seçin.";
    }

    private sealed class Counter
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public KkdDefinitionWorkbookCategoryResult ToResult() => new(Created, Updated, Unchanged);
    }

    private sealed record CustomerReference(long Id, string Code, string Name);
    private sealed record StockReference(long Id, string Code, string Name, string? GroupCode, string UnitCode);
    private sealed record UserReference(long Id, string Username, string Email, bool IsActive);
    private sealed record DepartmentRow(int Row, long? Id, string Code, string Name, bool IsActive);
    private sealed record RoleRow(int Row, long? Id, string? DepartmentCode, string Code, string Name, bool IsActive);
    private sealed record EmployeeRow(int Row, long? Id, string EmployeeCode, string FirstName, string LastName,
        string CustomerCode, string DepartmentCode, string RoleCode, string QrCode, DateOnly EmploymentStartDate,
        string? UserLogin, bool IsActive);
    private sealed record MatrixRow(int Row, long? Id, string Code, string Name, string CustomerCode,
        string DepartmentCode, string RoleCode, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? Description);
    private sealed record RuleRow(int Row, long? Id, string MatrixCode, string GroupCode, string? GroupName,
        string? StockCode, string? StandardCode, string? StandardName, int? AnnualIssueCount, decimal? AnnualQuantity,
        decimal? MaxCarryQuantity, bool AllowBulkIssue, bool IsMandatory, int SortOrder, bool IsActive, string? Description);
    private sealed record PhaseRow(int Row, long? Id, string MatrixCode, string? StockCode, string GroupCode,
        string PhaseType, int OffsetMonths, decimal Quantity, bool AllowBulkIssue, int? FrequencyDays,
        decimal? QuantityPerFrequency, string? PeriodType, int? PeriodInterval, int SortOrder, bool IsActive, string? Description);
}
