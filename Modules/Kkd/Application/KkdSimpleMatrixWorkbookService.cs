using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed partial class KkdSimpleMatrixWorkbookService(
    IUnitOfWork uow,
    IKkdDefinitionService definitions) : IKkdSimpleMatrixWorkbookService
{
    public const int MaxRows = 5_000;
    public const int MaxColumns = 500;
    public const int MaxFileSize = 15 * 1024 * 1024;

    private const string DataSheet = "Liste";
    private const string GuideSheet = "KILAVUZ";
    private const string MetaSheet = "__WMS_META";
    private const string TemplateType = "KKD_SIMPLE_WIDE_MATRIX";
    private const int TemplateVersion = 1;
    private const int FirstDataRow = 5;
    private const int FirstQuantityColumn = 4;

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<byte[]> CreateTemplateAsync(
        long customerId,
        string branchCode,
        CancellationToken ct = default)
    {
        using var branchScope = uow.BeginBranchScope(NormalizeBranch(branchCode));
        var customer = await uow.Repository<CustomerEntity>().Query()
            .SingleOrDefaultAsync(x => x.Id == customerId, ct)
            ?? throw AppException.NotFound("Basit KKD şablonu için seçilen cari bulunamadı.");
        var matrices = await uow.Repository<KkdEntitlementMatrix>().Query()
            .AsSplitQuery()
            .Include(x => x.Department)
            .Include(x => x.Role)
            .Include(x => x.Rules).ThenInclude(x => x.Phases)
            .Where(x => x.CustomerId == customerId && x.IsActive)
            .OrderBy(x => x.Department.Code).ThenBy(x => x.Role.Code).ThenBy(x => x.Code)
            .ToListAsync(ct);
        var departments = await uow.Repository<KkdDepartment>().Query()
            .Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync(ct);
        var roles = await uow.Repository<KkdRole>().Query()
            .Include(x => x.Department).Where(x => x.IsActive)
            .OrderBy(x => x.Department!.Code).ThenBy(x => x.Code).ToListAsync(ct);
        var stocks = await uow.Repository<StockEntity>().Query()
            .OrderBy(x => x.ErpStockCode)
            .Select(x => new StockReference(x.Id, x.ErpStockCode, x.StockName, x.GroupCode))
            .ToListAsync(ct);

        var blocks = BuildTemplateBlocks(matrices);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(DataSheet);
        WriteSimpleHeaders(sheet, blocks);

        var rowNumber = FirstDataRow;
        foreach (var matrix in matrices
                     .GroupBy(x => new { x.DepartmentId, x.RoleId })
                     .Select(x => x.First()))
        {
            sheet.Cell(rowNumber, 1).Value = matrix.Department.Name;
            sheet.Cell(rowNumber, 2).Value = matrix.Role.Name;
            foreach (var block in blocks)
            {
                var rule = matrix.Rules.FirstOrDefault(x => !x.IsDeleted && RuleKey(x.StockCodeSnapshot, x.GroupCode) == block.RuleKey);
                if (rule is null) continue;
                foreach (var column in block.Phases)
                {
                    var phase = rule.Phases.FirstOrDefault(x => !x.IsDeleted && PhaseKey(x) == column.PhaseKey);
                    if (phase is not null) sheet.Cell(rowNumber, column.Column).Value = phase.Quantity;
                }
            }
            rowNumber++;
        }
        if (rowNumber == FirstDataRow) rowNumber++;

        FinishSimpleSheet(sheet, rowNumber - 1, blocks);
        CreateGuide(workbook, customer.CustomerCode, customer.CustomerName);
        CreateDepartmentRoleReference(workbook, departments, roles);
        CreateStockReference(workbook, stocks);
        CreateMetadata(workbook, customerId, customer.CustomerCode);
        workbook.Worksheet(DataSheet).Position = 1;

        await using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    public async Task<KkdSimpleMatrixWorkbookPreview> PreviewAsync(
        Stream workbookStream,
        long customerId,
        DateOnly effectiveFrom,
        string branchCode,
        CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(workbookStream, customerId, effectiveFrom, branchCode, ct);
        return ToPreview(prepared);
    }

    public async Task<KkdSimpleMatrixWorkbookImportResult> ImportAsync(
        Stream workbookStream,
        long customerId,
        DateOnly effectiveFrom,
        string branchCode,
        string previewHash,
        string stateHash,
        long actor,
        CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(workbookStream, customerId, effectiveFrom, branchCode, ct);
        if (prepared.Errors.Count > 0)
            throw AppException.BadRequest("Basit KKD matrisi hatalar içeriyor. Dosyayı yeniden önizleyip hataları düzeltin.");
        if (!string.Equals(prepared.FileHash, previewHash?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict("Yüklenen dosya önizlenen dosyayla aynı değil. Yeniden önizleme yapın.");
        if (!string.Equals(prepared.StateHash, stateHash?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict("KKD tanımları önizlemeden sonra değişti. Güncel verilerle yeniden önizleme yapın.");

        using var branchScope = uow.BeginBranchScope(NormalizeBranch(branchCode));
        var created = 0;
        var updated = 0;
        await uow.ExecuteInTransactionAsync(async token =>
        {
            foreach (var draft in prepared.Drafts)
            {
                await definitions.UpsertMatrixAsync(draft.ExistingId, draft.Request, actor, token);
                if (draft.ExistingId.HasValue) updated++; else created++;
            }
            return true;
        }, ct, IsolationLevel.ReadCommitted);

        return new(
            prepared.FileHash,
            created,
            updated,
            created + updated,
            prepared.Warnings.Select(x => x.Message).Distinct().ToArray());
    }

    private async Task<PreparedImport> PrepareAsync(
        Stream source,
        long customerId,
        DateOnly effectiveFrom,
        string branchCode,
        CancellationToken ct)
    {
        var bytes = await ReadBytesAsync(source, ct);
        var fileHash = Convert.ToHexString(SHA256.HashData(bytes));
        using var workbook = OpenWorkbook(bytes);
        var errors = new List<KkdSimpleMatrixWorkbookIssue>();
        var warnings = new List<KkdSimpleMatrixWorkbookIssue>();
        var sheet = ResolveDataSheet(workbook);
        var used = sheet.RangeUsed();
        if (used is null)
            return EmptyPrepared(fileHash, errors, warnings, "Aktarılacak veri bulunamadı.");
        if (used.RowCount() > MaxRows + FirstDataRow - 1)
            errors.Add(Issue("ROW_LIMIT", $"En fazla {MaxRows:N0} görev satırı aktarılabilir.", sheet.Name));
        if (used.ColumnCount() > MaxColumns)
            errors.Add(Issue("COLUMN_LIMIT", $"En fazla {MaxColumns:N0} kolon aktarılabilir.", sheet.Name));
        ValidateDescriptorHeaders(sheet, errors);
        ValidateMetadata(workbook, customerId, errors);

        using var branchScope = uow.BeginBranchScope(NormalizeBranch(branchCode));
        var customer = await uow.Repository<CustomerEntity>().Query()
            .SingleOrDefaultAsync(x => x.Id == customerId, ct);
        if (customer is null)
        {
            errors.Add(Issue("CUSTOMER_NOT_FOUND", "Seçilen cari bulunamadı veya bu şubede kullanılamıyor."));
            return new(fileHash, string.Empty, 0, [], errors, warnings, 0, 0, 0, 0);
        }

        var departments = await uow.Repository<KkdDepartment>().Query().Where(x => x.IsActive).ToListAsync(ct);
        var roles = await uow.Repository<KkdRole>().Query().Where(x => x.IsActive).ToListAsync(ct);
        var stocks = await uow.Repository<StockEntity>().Query()
            .Select(x => new StockReference(x.Id, x.ErpStockCode, x.StockName, x.GroupCode))
            .ToListAsync(ct);
        var matrices = await uow.Repository<KkdEntitlementMatrix>().Query()
            .Where(x => x.CustomerId == customerId)
            .ToListAsync(ct);

        var blocks = ParseBlocks(sheet, stocks, errors, warnings);
        var sourceRows = ParseSourceRows(sheet, used.LastRow().RowNumber(), blocks, departments, roles, errors);
        var workerClasses = sourceRows.Select(x => x.WorkerClass).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        if (workerClasses.Length > 0)
            warnings.Add(Issue(
                "WORKER_CLASS_INFORMATIONAL",
                $"BY/MY alanı WMS hak anahtarının parçası değildir ve bilgi amaçlı okunur: {string.Join(", ", workerClasses)}.",
                sheet.Name));
        var duplicateCount = 0;
        var uniqueRows = new List<SourceRow>();
        foreach (var group in sourceRows.Where(x => x.Department is not null && x.Role is not null)
                     .GroupBy(x => $"{x.Department!.Id}|{x.Role!.Id}"))
        {
            var first = group.First();
            var duplicates = group.Skip(1).ToArray();
            if (duplicates.Length == 0) { uniqueRows.Add(first); continue; }
            duplicateCount += duplicates.Length;
            if (duplicates.Any(x => !SameQuantities(first.Values, x.Values)))
            {
                errors.Add(Issue(
                    "CONFLICTING_ROLE_ROWS",
                    $"'{first.Department!.Name} / {first.Role!.Name}' için birden fazla ve farklı hak satırı var. WMS aynı bölüm/görev için tek matris tutar.",
                    sheet.Name,
                    first.Row));
                continue;
            }
            warnings.Add(Issue(
                "DUPLICATE_ROLE_ROWS",
                $"'{first.Department!.Name} / {first.Role!.Name}' için aynı içerikte {duplicates.Length + 1} satır bulundu; tek satır olarak değerlendirildi.",
                sheet.Name,
                first.Row));
            uniqueRows.Add(first);
        }

        var drafts = new List<MatrixDraft>();
        var relevantMatrixIds = new HashSet<long>();
        var relevantStocks = new HashSet<long>();
        foreach (var block in blocks.Where(x => x.Stock is not null)) relevantStocks.Add(block.Stock!.Id);

        foreach (var row in uniqueRows)
        {
            var department = row.Department!;
            var role = row.Role!;
            var candidates = matrices.Where(x => x.DepartmentId == department.Id && x.RoleId == role.Id
                && x.IsActive
                && (!x.EffectiveFrom.HasValue || x.EffectiveFrom <= effectiveFrom)
                && (!x.EffectiveTo.HasValue || x.EffectiveTo >= effectiveFrom)).ToArray();
            if (candidates.Length > 1)
            {
                errors.Add(Issue(
                    "OVERLAPPING_MATRICES",
                    $"'{department.Name} / {role.Name}' için {effectiveFrom:dd.MM.yyyy} tarihinde birden fazla aktif matris var.",
                    sheet.Name,
                    row.Row));
                continue;
            }

            KkdMatrixDetail? existing = null;
            if (candidates.Length == 1)
            {
                relevantMatrixIds.Add(candidates[0].Id);
                existing = await definitions.GetMatrixAsync(candidates[0].Id, ct);
            }

            var imported = BuildImportedRules(row, blocks, existing, errors, warnings);
            if (imported.Count == 0 && existing is null) continue;
            var merged = MergeRules(existing, blocks, imported);
            var hasPositive = imported.Count > 0;
            var request = existing is null
                ? new KkdMatrixUpsertRequest(
                    customer.Id,
                    department.Id,
                    role.Id,
                    NewMatrixCode(customer.Id, department.Id, role.Id, effectiveFrom),
                    $"{customer.CustomerName} · {department.Name} · {role.Name}",
                    effectiveFrom,
                    null,
                    hasPositive,
                    "Basit geniş KKD matrisi aktarımıyla oluşturuldu.",
                    merged,
                    null)
                : new KkdMatrixUpsertRequest(
                    existing.CustomerId,
                    existing.DepartmentId,
                    existing.RoleId,
                    existing.Code,
                    existing.Name,
                    existing.EffectiveFrom,
                    existing.EffectiveTo,
                    hasPositive || merged.Count > 0,
                    existing.Description,
                    merged.Count > 0 ? merged : existing.Rules.Select(ToRequest).ToArray(),
                    Convert.ToBase64String(existing.RowVersion));

            var validation = await definitions.ValidateMatrixAsync(existing?.Id, request, ct);
            foreach (var issue in validation.Issues)
                errors.Add(Issue("MATRIX_VALIDATION", issue.Message, sheet.Name, row.Row));
            if (validation.IsValid) drafts.Add(new(existing?.Id, request));
        }

        var stateHash = BuildStateHash(customerId, effectiveFrom, departments, roles, stocks, matrices, relevantMatrixIds, relevantStocks);
        return new(
            fileHash,
            stateHash,
            sourceRows.Count,
            drafts,
            errors.DistinctBy(x => $"{x.Code}|{x.Cell}|{x.Message}").ToArray(),
            warnings.DistinctBy(x => $"{x.Code}|{x.Cell}|{x.Message}").ToArray(),
            duplicateCount,
            drafts.Sum(x => x.Request.Rules.Count),
            drafts.Sum(x => x.Request.Rules.Sum(r => r.Phases.Count)),
            drafts.Count(x => !x.ExistingId.HasValue));
    }

    private static List<KkdRuleUpsertRequest> BuildImportedRules(
        SourceRow row,
        IReadOnlyList<ProductBlock> blocks,
        KkdMatrixDetail? existing,
        ICollection<KkdSimpleMatrixWorkbookIssue> errors,
        ICollection<KkdSimpleMatrixWorkbookIssue> warnings)
    {
        var result = new List<KkdRuleUpsertRequest>();
        var existingByKey = existing?.Rules.ToDictionary(x => RuleKey(x.StockCode, x.GroupCode), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, KkdRuleDetail>(StringComparer.OrdinalIgnoreCase);
        var seenPhases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sort = 0;
        foreach (var block in blocks)
        {
            var positive = block.Phases
                .Select(x => new { Column = x, Quantity = row.Values.TryGetValue(x.Column, out var value) ? value : null })
                .Where(x => x.Quantity is > 0)
                .ToArray();
            if (positive.Length == 0) continue;
            if (positive.Any(x => x.Column.PhaseType == nameof(KkdEntitlementPhaseType.Initial))
                && positive.Any(x => x.Column.PhaseType == nameof(KkdEntitlementPhaseType.Recurring)
                    && x.Column.PeriodType == nameof(KkdPeriodType.Day)))
                warnings.Add(Issue(
                    "DAILY_ROUTINE_OVERRIDES_INITIAL",
                    $"'{block.Name}' için günlük/haftalık rutin işe giriş gününde başlar; İlk Hak aynı gün ayrıca ek hak oluşturmaz.",
                    DataSheet,
                    row.Row,
                    CellAddress(row.Row, block.StartColumn)));
            if (block.Stock is null && string.IsNullOrWhiteSpace(block.GroupCode))
            {
                errors.Add(Issue(
                    "PRODUCT_NOT_RESOLVED",
                    $"'{block.Name}' için stok kodu veya GRUP:kod bilgisi çözülemedi.",
                    DataSheet,
                    row.Row,
                    CellAddress(row.Row, block.StartColumn)));
                continue;
            }
            var key = RuleKey(block.Stock?.Code, block.GroupCode ?? block.Stock?.GroupCode);
            existingByKey.TryGetValue(key, out var current);
            var phases = new List<KkdPhaseUpsertRequest>();
            foreach (var item in positive)
            {
                var phaseKey = $"{key}|{item.Column.PhaseType}|{item.Column.OffsetMonths}";
                if (!seenPhases.Add(phaseKey))
                {
                    errors.Add(Issue(
                        "DUPLICATE_PRODUCT_PHASE",
                        $"'{block.Name}' için aynı dönem birden fazla kolonda pozitif miktar içeriyor.",
                        DataSheet,
                        row.Row,
                        CellAddress(row.Row, item.Column.Column)));
                    continue;
                }
                if (item.Quantity > 100)
                    warnings.Add(Issue(
                        "OUTLIER_QUANTITY",
                        $"'{block.Name}' için {item.Quantity:N2} miktarı sıra dışı görünüyor; dönem başına miktar olduğundan emin olun.",
                        DataSheet,
                        row.Row,
                        CellAddress(row.Row, item.Column.Column)));
                phases.Add(new(
                    item.Column.PhaseType,
                    item.Column.OffsetMonths,
                    item.Quantity!.Value,
                    current?.AllowBulkIssue ?? true,
                    item.Column.FrequencyDays,
                    item.Column.QuantityPerFrequency,
                    item.Column.PeriodType,
                    item.Column.PeriodInterval,
                    item.Column.Column,
                    true,
                    $"Kaynak kolon: {item.Column.Header}"));
            }
            if (phases.Count == 0) continue;
            result.Add(new(
                block.GroupCode ?? block.Stock!.GroupCode ?? string.Empty,
                current?.GroupName ?? block.Name,
                block.Stock?.Id,
                string.IsNullOrWhiteSpace(block.Standard) ? current?.StandardCode : TrimTo(block.Standard, 80),
                current?.StandardName,
                current?.AnnualIssueCount,
                current?.AnnualQuantity,
                current?.MaxCarryQuantity,
                current?.AllowBulkIssue ?? true,
                current?.IsMandatory ?? false,
                sort++,
                true,
                current?.Description,
                phases));
        }
        return result;
    }

    private static IReadOnlyList<KkdRuleUpsertRequest> MergeRules(
        KkdMatrixDetail? existing,
        IReadOnlyList<ProductBlock> blocks,
        IReadOnlyList<KkdRuleUpsertRequest> imported)
    {
        if (existing is null) return imported;
        var importedByKey = imported
            .GroupBy(
                x => x.StockId.HasValue
                    ? blocks.First(block => block.Stock?.Id == x.StockId.Value).RuleIdentity
                    : RuleKey(null, x.GroupCode),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var scope = blocks
            .Where(x => x.Stock is not null || !string.IsNullOrWhiteSpace(x.GroupCode))
            .Select(x => RuleKey(x.Stock?.Code, x.GroupCode ?? x.Stock?.GroupCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<KkdRuleUpsertRequest>();
        foreach (var current in existing.Rules)
        {
            var key = RuleKey(current.StockCode, current.GroupCode);
            if (importedByKey.Remove(key, out var replacement)) result.Add(replacement);
            else if (!scope.Contains(key)) result.Add(ToRequest(current));
        }
        result.AddRange(importedByKey.Values);
        return result.OrderBy(x => x.SortOrder).ToArray();
    }

    private static KkdRuleUpsertRequest ToRequest(KkdRuleDetail x) => new(
        x.GroupCode,
        x.GroupName,
        x.StockId,
        x.StandardCode,
        x.StandardName,
        x.AnnualIssueCount,
        x.AnnualQuantity,
        x.MaxCarryQuantity,
        x.AllowBulkIssue,
        x.IsMandatory,
        x.SortOrder,
        x.IsActive,
        x.Description,
        x.Phases.Select(p => new KkdPhaseUpsertRequest(
            p.PhaseType,
            p.OffsetMonths,
            p.Quantity,
            p.AllowBulkIssue,
            p.FrequencyDays,
            p.QuantityPerFrequency,
            p.PeriodType,
            p.PeriodInterval,
            p.SortOrder,
            p.IsActive,
            p.Description)).ToArray());

    private static List<ProductBlock> ParseBlocks(
        IXLWorksheet sheet,
        IReadOnlyList<StockReference> stocks,
        ICollection<KkdSimpleMatrixWorkbookIssue> errors,
        ICollection<KkdSimpleMatrixWorkbookIssue> warnings)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? FirstQuantityColumn - 1;
        if (lastColumn < FirstQuantityColumn)
        {
            errors.Add(Issue("PRODUCT_COLUMNS_MISSING", "En az bir KKD ürün kolonu bulunmalıdır.", sheet.Name));
            return [];
        }
        var starts = Enumerable.Range(FirstQuantityColumn, lastColumn - FirstQuantityColumn + 1)
            .Where(x => !string.IsNullOrWhiteSpace(Text(sheet.Cell(1, x))))
            .ToArray();
        if (starts.Length == 0)
        {
            errors.Add(Issue("PRODUCT_HEADERS_MISSING", "1. satırda KKD ürün başlıkları bulunamadı.", sheet.Name, 1));
            return [];
        }

        var stocksByCode = stocks
            .GroupBy(x => Normalize(x.Code))
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var result = new List<ProductBlock>();
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var end = (index + 1 < starts.Length ? starts[index + 1] : lastColumn + 1) - 1;
            var name = Text(sheet.Cell(1, start));
            var standard = Text(sheet.Cell(2, start));
            var identifier = Text(sheet.Cell(3, start));
            string? groupCode = null;
            StockReference? stock = null;
            if (identifier.StartsWith("GRUP:", StringComparison.OrdinalIgnoreCase))
                groupCode = identifier[5..].Trim();
            else
            {
                var code = ResolveStockCode(identifier, stocksByCode);
                if (code is not null && stocksByCode.TryGetValue(Normalize(code), out var matches) && matches.Length == 1)
                    stock = matches[0];
            }

            var phases = new List<PhaseColumn>();
            for (var column = start; column <= end; column++)
            {
                var header = Text(sheet.Cell(4, column));
                if (header.Length == 0)
                {
                    errors.Add(Issue("PHASE_HEADER_REQUIRED", $"'{name}' ürün grubunda dönem başlığı boş olamaz.", sheet.Name, 4, CellAddress(4, column)));
                    continue;
                }
                var parsed = ParsePhaseHeader(header, name, column, warnings);
                if (parsed is null)
                {
                    errors.Add(Issue("PHASE_HEADER_INVALID", $"'{header}' dönem başlığı tanınmadı.", sheet.Name, 4, CellAddress(4, column)));
                    continue;
                }
                phases.Add(parsed);
            }
            result.Add(new(start, end, name, standard, identifier, groupCode, stock, RuleKey(stock?.Code, groupCode ?? stock?.GroupCode), phases));
        }
        foreach (var duplicate in result
                     .Where(x => x.Stock is not null || !string.IsNullOrWhiteSpace(x.GroupCode))
                     .GroupBy(x => x.RuleIdentity, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
        {
            var columns = string.Join(", ", duplicate.Select(x => CellAddress(3, x.StartColumn)));
            errors.Add(Issue(
                "DUPLICATE_PRODUCT_IDENTIFIER",
                $"Aynı stok/grup birden fazla ürün bloğunda kullanılmış ({columns}). Tek ürün bloğunda birleştirin.",
                sheet.Name,
                3,
                CellAddress(3, duplicate.First().StartColumn)));
        }
        return result;
    }

    private static List<SourceRow> ParseSourceRows(
        IXLWorksheet sheet,
        int lastRow,
        IReadOnlyList<ProductBlock> blocks,
        IReadOnlyList<KkdDepartment> departments,
        IReadOnlyList<KkdRole> roles,
        ICollection<KkdSimpleMatrixWorkbookIssue> errors)
    {
        var quantityColumns = blocks.SelectMany(x => x.Phases).Select(x => x.Column).Distinct().ToArray();
        var result = new List<SourceRow>();
        for (var rowNumber = FirstDataRow; rowNumber <= Math.Min(lastRow, MaxRows + FirstDataRow - 1); rowNumber++)
        {
            var departmentText = Text(sheet.Cell(rowNumber, 1));
            var roleText = Text(sheet.Cell(rowNumber, 2));
            var workerClass = Text(sheet.Cell(rowNumber, 3));
            var hasQuantity = quantityColumns.Any(column => !sheet.Cell(rowNumber, column).IsEmpty());
            if (departmentText.Length == 0 && roleText.Length == 0 && !hasQuantity) continue;
            var department = ResolveUnique(
                departments,
                x => x.Code,
                x => x.Name,
                departmentText,
                "bölüm",
                sheet.Name,
                rowNumber,
                1,
                errors);
            var roleCandidates = department is null ? [] : roles.Where(x => x.DepartmentId == department.Id).ToArray();
            var role = ResolveUnique(
                roleCandidates,
                x => x.Code,
                x => x.Name,
                roleText,
                "görev/rol",
                sheet.Name,
                rowNumber,
                2,
                errors);
            var values = new Dictionary<int, decimal?>();
            foreach (var column in quantityColumns)
            {
                var cell = sheet.Cell(rowNumber, column);
                if (cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.GetFormattedString()))
                {
                    values[column] = null;
                    continue;
                }
                if (!TryDecimal(cell, out var value) || value < 0)
                {
                    errors.Add(Issue("QUANTITY_INVALID", "Miktar sıfır veya pozitif sayı olmalıdır.", sheet.Name, rowNumber, CellAddress(rowNumber, column)));
                    values[column] = null;
                    continue;
                }
                values[column] = value;
            }
            result.Add(new(rowNumber, departmentText, roleText, workerClass, department, role, values));
        }
        if (result.Count == 0)
            errors.Add(Issue("DATA_ROWS_MISSING", "Aktarılacak bölüm/görev satırı bulunamadı.", sheet.Name));
        return result;
    }

    private static T? ResolveUnique<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> code,
        Func<T, string> name,
        string input,
        string label,
        string sheet,
        int row,
        int column,
        ICollection<KkdSimpleMatrixWorkbookIssue> errors) where T : class
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            errors.Add(Issue("LOOKUP_REQUIRED", $"{label} zorunludur.", sheet, row, CellAddress(row, column)));
            return null;
        }
        var normalized = Normalize(input);
        var matches = candidates.Where(x => Normalize(code(x)) == normalized || Normalize(name(x)) == normalized).ToArray();
        if (matches.Length == 1) return matches[0];
        errors.Add(Issue(
            matches.Length == 0 ? "LOOKUP_NOT_FOUND" : "LOOKUP_AMBIGUOUS",
            matches.Length == 0
                ? $"'{input}' {label} olarak bulunamadı. Sistem kodunu veya tam adını kullanın."
                : $"'{input}' birden fazla {label} ile eşleşiyor. Sistem kodunu kullanın.",
            sheet,
            row,
            CellAddress(row, column)));
        return null;
    }

    private static PhaseColumn? ParsePhaseHeader(
        string header,
        string productName,
        int column,
        ICollection<KkdSimpleMatrixWorkbookIssue> warnings)
    {
        var value = Normalize(header);
        if (value == Normalize(productName))
        {
            warnings.Add(Issue(
                "PRODUCT_NAME_USED_AS_PHASE",
                $"'{header}' dönem başlığı ürün adıyla aynı olduğu için 'İlk Hak' kabul edildi.",
                DataSheet,
                4,
                CellAddress(4, column)));
            return Initial(column, header);
        }
        if ((value.Contains("İLK") || value.Contains("ILK"))
            && (value.Contains("GİRİŞ") || value.Contains("GIRIS") || value.Contains("HAK")))
            return Initial(column, header);

        var monthMatch = MonthOffsetRegex().Match(value);
        if (monthMatch.Success && (value.Contains("SONRA") || value.Contains("İTİBAREN") || value.Contains("ITIBAREN")))
            return new(column, header, nameof(KkdEntitlementPhaseType.AfterMonths), int.Parse(monthMatch.Groups[1].Value), null, null, null, null);
        if (!value.Contains("RUTİN") && !value.Contains("RUTIN") && !value.Contains("DÖNEM")) return null;

        var dayMatch = DayPeriodRegex().Match(value);
        if (dayMatch.Success)
        {
            var interval = int.Parse(dayMatch.Groups[1].Value);
            return Recurring(column, header, nameof(KkdPeriodType.Day), interval, 0);
        }
        if (value.Contains("GÜNDE") || value.Contains("GUNDE"))
            return Recurring(column, header, nameof(KkdPeriodType.Day), 1, 0);
        var weekMatch = WeekPeriodRegex().Match(value);
        if (weekMatch.Success)
            return Recurring(column, header, nameof(KkdPeriodType.Day), int.Parse(weekMatch.Groups[1].Value) * 7, 0);
        if (value.Contains("HAFTADA"))
            return Recurring(column, header, nameof(KkdPeriodType.Day), 7, 0);
        var monthPeriodMatch = MonthPeriodRegex().Match(value);
        if (monthPeriodMatch.Success)
        {
            var interval = int.Parse(monthPeriodMatch.Groups[1].Value);
            return Recurring(column, header, nameof(KkdPeriodType.Month), interval, interval);
        }
        if (value.Contains("AYDA") || value.Contains("AYLIK"))
            return Recurring(column, header, nameof(KkdPeriodType.Month), 1, 1);
        var yearPeriodMatch = YearPeriodRegex().Match(value);
        if (yearPeriodMatch.Success)
        {
            var interval = int.Parse(yearPeriodMatch.Groups[1].Value);
            return Recurring(column, header, nameof(KkdPeriodType.Year), interval, interval * 12);
        }
        if (value.Contains("YIL"))
            return Recurring(column, header, nameof(KkdPeriodType.Year), 1, 12);

        warnings.Add(Issue(
            "ROUTINE_PERIOD_DEFAULTED",
            $"'{header}' başlığında süre olmadığı için yıllık dönem kabul edildi.",
            DataSheet,
            4,
            CellAddress(4, column)));
        return Recurring(column, header, nameof(KkdPeriodType.Year), 1, 12);
    }

    private static PhaseColumn Initial(int column, string header) =>
        new(column, header, nameof(KkdEntitlementPhaseType.Initial), 0, null, null, null, null);

    private static PhaseColumn Recurring(int column, string header, string periodType, int interval, int offsetMonths) =>
        new(column, header, nameof(KkdEntitlementPhaseType.Recurring), offsetMonths, periodType, interval, null, null);

    private static IReadOnlyList<TemplateBlock> BuildTemplateBlocks(IReadOnlyList<KkdEntitlementMatrix> matrices)
    {
        var column = FirstQuantityColumn;
        var blocks = matrices
            .SelectMany(x => x.Rules.Where(r => !r.IsDeleted))
            .GroupBy(x => RuleKey(x.StockCodeSnapshot, x.GroupCode), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.OrderBy(x => x.SortOrder).First();
                var phases = group.SelectMany(x => x.Phases.Where(p => !p.IsDeleted))
                    .GroupBy(PhaseKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .OrderBy(x => x.OffsetMonths).ThenBy(x => x.SortOrder)
                    .Select(x => new TemplatePhase(column++, PhaseKey(x), PhaseLabel(x)))
                    .ToArray();
                return new TemplateBlock(
                    RuleKey(first.StockCodeSnapshot, first.GroupCode),
                    first.StockNameSnapshot ?? first.GroupName ?? first.GroupCode,
                    first.StandardCode,
                    first.StockCodeSnapshot ?? $"GRUP:{first.GroupCode}",
                    phases);
            })
            .Where(x => x.Phases.Count > 0)
            .ToArray();
        if (blocks.Length > 0) return blocks;
        return
        [
            new("S:STOK-KODU-1", "KKD 1 — ürün adını değiştirin", null, "STOK-KODU-1",
            [
                new(column++, "Initial|0", "İLK YENİ GİRİŞ"),
                new(column++, "Recurring|12|Year|1", "RUTİNDE HER DÖNEM (YILDA 1)")
            ]),
            new("S:STOK-KODU-2", "KKD 2 — ürün adını değiştirin", null, "STOK-KODU-2",
            [
                new(column++, "Initial|0", "İLK YENİ GİRİŞ"),
                new(column, "Recurring|12|Year|1", "RUTİNDE HER DÖNEM (YILDA 1)")
            ])
        ];
    }

    private static void WriteSimpleHeaders(IXLWorksheet sheet, IReadOnlyList<TemplateBlock> blocks)
    {
        sheet.Cell(4, 1).Value = "Bölüm";
        sheet.Cell(4, 2).Value = "Görev Tanımı";
        sheet.Cell(4, 3).Value = "BY/MY";
        foreach (var block in blocks)
        {
            var start = block.Phases.Min(x => x.Column);
            var end = block.Phases.Max(x => x.Column);
            if (end > start)
            {
                sheet.Range(1, start, 1, end).Merge();
                sheet.Range(2, start, 2, end).Merge();
                sheet.Range(3, start, 3, end).Merge();
            }
            sheet.Cell(1, start).Value = block.Name;
            sheet.Cell(2, start).Value = block.Standard ?? string.Empty;
            sheet.Cell(3, start).Value = block.Identifier;
            foreach (var phase in block.Phases) sheet.Cell(4, phase.Column).Value = phase.Label;
        }
    }

    private static void FinishSimpleSheet(IXLWorksheet sheet, int dataLastRow, IReadOnlyList<TemplateBlock> blocks)
    {
        var lastColumn = Math.Max(3, blocks.SelectMany(x => x.Phases).Max(x => x.Column));
        var lastRow = Math.Max(FirstDataRow, dataLastRow);
        var header = sheet.Range(1, 1, 4, lastColumn);
        header.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold()
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Alignment.SetWrapText();
        sheet.Range(1, 1, 3, 3).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E2E8F0"));
        sheet.Range(4, 1, 4, 3).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#0E7490"));
        sheet.Range(FirstDataRow, 1, lastRow, lastColumn).Style.Border
            .SetBottomBorder(XLBorderStyleValues.Hair)
            .Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));
        sheet.Range(FirstDataRow, FirstQuantityColumn, Math.Max(lastRow, FirstDataRow + 199), lastColumn)
            .Style.NumberFormat.Format = "0.######";
        var classValidation = sheet.Range(FirstDataRow, 3, Math.Max(lastRow, FirstDataRow + 199), 3).CreateDataValidation();
        classValidation.List("\"MY,Saha BY,Ofis BY\"", true);
        classValidation.IgnoreBlanks = true;
        sheet.Range(4, 1, lastRow, lastColumn).SetAutoFilter();
        sheet.SheetView.FreezeRows(4);
        sheet.SheetView.FreezeColumns(3);
        sheet.Column(1).Width = 24;
        sheet.Column(2).Width = 36;
        sheet.Column(3).Width = 14;
        for (var column = FirstQuantityColumn; column <= lastColumn; column++) sheet.Column(column).Width = 16;
        sheet.Rows(1, 4).Height = 34;
        sheet.TabColor = XLColor.FromHtml("#0E7490");
    }

    private static void CreateGuide(XLWorkbook workbook, string customerCode, string customerName)
    {
        var sheet = workbook.Worksheets.Add(GuideSheet);
        sheet.Range("A1:F2").Merge();
        sheet.Cell("A1").Value = "WMS V2 — Basit KKD Hak Matrisi";
        sheet.Range("A1:F2").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(17)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        var rows = new[]
        {
            new[] { "Cari", $"{customerCode} · {customerName}" },
            new[] { "1. Satırlar", "Her satır bir bölüm + görev hak matrisidir. Bölüm ve görevi sistem kodu veya tam adıyla yazın." },
            new[] { "2. Kolonlar", "Her KKD başlığının altında ilk hak, ay sonrası veya rutin dönem kolonları bulunur." },
            new[] { "3. Miktar", "Hücredeki sayı dönem başına verilebilecek miktardır. Boş ve 0 değerler hak oluşturmaz." },
            new[] { "4. Stok", "3. satırdaki stok kodu sistemde birebir bulunmalıdır. Grup kuralı için GRUP:KOD biçimini kullanın." },
            new[] { "5. Güvenlik", "Dosya önce önizlenir. Hatalar çözülmeden kayıt yapılmaz; önizlenen dosya değişirse commit reddedilir." },
            new[] { "6. Güncelleme", "Dosyada bulunan ürün kapsamı güncellenir. Dosyada hiç bulunmayan mevcut KKD kuralları korunur." },
            new[] { "7. BY/MY", "Bilgi amaçlıdır. Aynı bölüm/görev farklı haklarla birden fazla yazılırsa aktarım engellenir." }
        };
        var row = 4;
        foreach (var item in rows)
        {
            sheet.Cell(row, 1).Value = item[0];
            sheet.Range(row, 2, row, 6).Merge();
            sheet.Cell(row, 2).Value = item[1];
            sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#0E7490"));
            sheet.Range(row, 1, row, 6).Style.Fill.SetBackgroundColor(row % 2 == 0 ? XLColor.FromHtml("#ECFEFF") : XLColor.White)
                .Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top);
            sheet.Row(row).Height = 38;
            row++;
        }
        sheet.Column(1).Width = 22;
        sheet.Columns(2, 6).Width = 28;
        sheet.Column(2).Width = 52;
        sheet.TabColor = XLColor.FromHtml("#38BDF8");
    }

    private static void CreateDepartmentRoleReference(
        XLWorkbook workbook,
        IReadOnlyList<KkdDepartment> departments,
        IReadOnlyList<KkdRole> roles)
    {
        var sheet = workbook.Worksheets.Add("REF_BOLUM_GOREV");
        WriteReferenceHeader(sheet, ["Bölüm Kodu", "Bölüm Adı", "Görev/Rol Kodu", "Görev/Rol Adı"]);
        var row = 2;
        foreach (var role in roles)
        {
            var department = departments.FirstOrDefault(x => x.Id == role.DepartmentId);
            if (department is null) continue;
            sheet.Cell(row, 1).Value = department.Code;
            sheet.Cell(row, 2).Value = department.Name;
            sheet.Cell(row, 3).Value = role.Code;
            sheet.Cell(row, 4).Value = role.Name;
            row++;
        }
        FinishReference(sheet, row - 1, 4);
    }

    private static void CreateStockReference(XLWorkbook workbook, IReadOnlyList<StockReference> stocks)
    {
        var sheet = workbook.Worksheets.Add("REF_STOKLAR");
        WriteReferenceHeader(sheet, ["Stok Kodu", "Stok Adı", "Stok Grup Kodu"]);
        var row = 2;
        foreach (var stock in stocks)
        {
            sheet.Cell(row, 1).Value = stock.Code;
            sheet.Cell(row, 2).Value = stock.Name;
            sheet.Cell(row, 3).Value = stock.GroupCode ?? string.Empty;
            row++;
        }
        FinishReference(sheet, row - 1, 3);
    }

    private static void CreateMetadata(XLWorkbook workbook, long customerId, string customerCode)
    {
        var sheet = workbook.Worksheets.Add(MetaSheet);
        sheet.Cell("A1").Value = "TemplateType";
        sheet.Cell("B1").Value = TemplateType;
        sheet.Cell("A2").Value = "TemplateVersion";
        sheet.Cell("B2").Value = TemplateVersion;
        sheet.Cell("A3").Value = "CustomerId";
        sheet.Cell("B3").Value = customerId;
        sheet.Cell("A4").Value = "CustomerCode";
        sheet.Cell("B4").Value = customerCode;
        sheet.Visibility = XLWorksheetVisibility.VeryHidden;
    }

    private static void WriteReferenceHeader(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++) sheet.Cell(1, index + 1).Value = headers[index];
        sheet.Range(1, 1, 1, headers.Count).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#475569"))
            .Font.SetFontColor(XLColor.White).Font.SetBold();
    }

    private static void FinishReference(IXLWorksheet sheet, int dataLastRow, int columnCount)
    {
        var lastRow = Math.Max(2, dataLastRow);
        sheet.Range(1, 1, lastRow, columnCount).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, columnCount).AdjustToContents(1, Math.Min(lastRow, 5000), 12, 42);
        sheet.TabColor = XLColor.FromHtml("#94A3B8");
        sheet.Protect();
    }

    private static void ValidateDescriptorHeaders(IXLWorksheet sheet, ICollection<KkdSimpleMatrixWorkbookIssue> errors)
    {
        var expected = new[] { "BÖLÜM", "GÖREV TANIMI", "BY/MY" };
        for (var index = 0; index < expected.Length; index++)
        {
            var actual = Normalize(Text(sheet.Cell(4, index + 1)));
            if (index == 1 && actual.StartsWith("GÖREV")) continue;
            if (actual == expected[index]) continue;
            errors.Add(Issue("DESCRIPTOR_HEADER_INVALID", $"{CellAddress(4, index + 1)} başlığı '{expected[index]}' olmalıdır.", sheet.Name, 4, CellAddress(4, index + 1)));
        }
    }

    private static void ValidateMetadata(
        XLWorkbook workbook,
        long selectedCustomerId,
        ICollection<KkdSimpleMatrixWorkbookIssue> errors)
    {
        var metadata = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(MetaSheet, StringComparison.OrdinalIgnoreCase));
        if (metadata is null) return;
        var templateType = Text(metadata.Cell("B1"));
        if (!templateType.Equals(TemplateType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(Issue("TEMPLATE_TYPE_INVALID", "Dosyanın basit KKD matris şablon tipi geçersiz.", metadata.Name, 1, "B1"));
            return;
        }
        if (!int.TryParse(Text(metadata.Cell("B2")), out var version) || version > TemplateVersion)
            errors.Add(Issue("TEMPLATE_VERSION_UNSUPPORTED", "Dosya bu WMS sürümünden daha yeni bir basit KKD şablonuyla oluşturulmuş.", metadata.Name, 2, "B2"));
        if (long.TryParse(Text(metadata.Cell("B3")), out var templateCustomerId) && templateCustomerId != selectedCustomerId)
            errors.Add(Issue("CUSTOMER_MISMATCH", "Dosya farklı bir cari için oluşturulmuş. Dosyanın carisiyle ekrandaki cariyi eşleştirin.", metadata.Name, 3, "B3"));
    }

    private static IXLWorksheet ResolveDataSheet(XLWorkbook workbook)
    {
        var named = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(DataSheet, StringComparison.OrdinalIgnoreCase));
        if (named is not null) return named;
        var detected = workbook.Worksheets.FirstOrDefault(x =>
            Normalize(Text(x.Cell("A4"))) == "BÖLÜM" && Normalize(Text(x.Cell("B4"))).StartsWith("GÖREV"));
        return detected ?? throw AppException.BadRequest("'Liste' sayfası veya Bölüm/Görev başlıklı basit KKD matrisi bulunamadı.");
    }

    private static string? ResolveStockCode(
        string raw,
        IReadOnlyDictionary<string, StockReference[]> stocksByCode)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (stocksByCode.ContainsKey(Normalize(raw))) return raw.Trim();
        foreach (var token in raw.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (stocksByCode.ContainsKey(Normalize(token))) return token.Trim();
        var match = StockCodeRegex().Match(raw);
        return match.Success && stocksByCode.ContainsKey(Normalize(match.Value)) ? match.Value : null;
    }

    private static bool TryDecimal(IXLCell cell, out decimal value)
    {
        if (cell.TryGetValue(out value)) return true;
        var text = cell.GetFormattedString().Trim();
        return decimal.TryParse(text, NumberStyles.Number, TurkishCulture, out value)
               || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool SameQuantities(
        IReadOnlyDictionary<int, decimal?> left,
        IReadOnlyDictionary<int, decimal?> right) =>
        left.Count == right.Count && left.All(x => right.TryGetValue(x.Key, out var value) && value == x.Value);

    private static KkdSimpleMatrixWorkbookPreview ToPreview(PreparedImport prepared) => new(
        prepared.FileHash,
        prepared.StateHash,
        prepared.Errors.Count == 0 && prepared.Drafts.Count > 0,
        prepared.SourceRowCount,
        prepared.Drafts.Count,
        prepared.RuleCount,
        prepared.PhaseCount,
        prepared.CreateCount,
        prepared.Drafts.Count - prepared.CreateCount,
        prepared.DuplicateRowCount,
        prepared.Errors,
        prepared.Warnings);

    private static PreparedImport EmptyPrepared(
        string fileHash,
        List<KkdSimpleMatrixWorkbookIssue> errors,
        List<KkdSimpleMatrixWorkbookIssue> warnings,
        string message)
    {
        errors.Add(Issue("EMPTY_WORKBOOK", message));
        return new(fileHash, string.Empty, 0, [], errors, warnings, 0, 0, 0, 0);
    }

    private static string BuildStateHash(
        long customerId,
        DateOnly effectiveFrom,
        IReadOnlyList<KkdDepartment> departments,
        IReadOnlyList<KkdRole> roles,
        IReadOnlyList<StockReference> stocks,
        IReadOnlyList<KkdEntitlementMatrix> matrices,
        IReadOnlySet<long> matrixIds,
        IReadOnlySet<long> stockIds)
    {
        var builder = new StringBuilder($"{customerId}|{effectiveFrom:O}");
        foreach (var department in departments.OrderBy(x => x.Id)) builder.Append($"|D:{department.Id}:{department.Code}:{department.UpdatedDate:O}");
        foreach (var role in roles.OrderBy(x => x.Id)) builder.Append($"|R:{role.Id}:{role.DepartmentId}:{role.Code}:{role.UpdatedDate:O}");
        foreach (var stock in stocks.Where(x => stockIds.Contains(x.Id)).OrderBy(x => x.Id)) builder.Append($"|S:{stock.Id}:{stock.Code}:{stock.GroupCode}");
        foreach (var matrix in matrices.Where(x => matrixIds.Contains(x.Id)).OrderBy(x => x.Id)) builder.Append($"|M:{matrix.Id}:{Convert.ToBase64String(matrix.RowVersion)}:{matrix.UpdatedDate:O}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string NewMatrixCode(long customerId, long departmentId, long roleId, DateOnly effectiveFrom) =>
        $"SIMPLE-{customerId}-{departmentId}-{roleId}-{effectiveFrom:yyyyMMdd}";

    private static string RuleKey(string? stockCode, string? groupCode) =>
        !string.IsNullOrWhiteSpace(stockCode) ? $"S:{Normalize(stockCode)}" : $"G:{Normalize(groupCode)}";

    private static string PhaseKey(KkdEntitlementPhase phase) =>
        $"{phase.PhaseType}|{phase.OffsetMonths}|{phase.PeriodType}|{phase.PeriodInterval}|{phase.FrequencyDays}";

    private static string PhaseLabel(KkdEntitlementPhase phase) => phase.PhaseType switch
    {
        KkdEntitlementPhaseType.Initial => "İLK YENİ GİRİŞ",
        KkdEntitlementPhaseType.AfterMonths => $"{phase.OffsetMonths}. AYDAN İTİBAREN",
        _ when phase.PeriodType == KkdPeriodType.Day => $"RUTİNDE HER DÖNEM ({phase.PeriodInterval ?? 1} GÜNDE 1)",
        _ when phase.PeriodType == KkdPeriodType.Month => $"RUTİNDE HER DÖNEM ({phase.PeriodInterval ?? 1} AYDA 1)",
        _ => $"RUTİNDE HER DÖNEM ({phase.PeriodInterval ?? 1} YILDA 1)"
    };

    private static string CellAddress(int row, int column) => XLHelper.GetColumnLetterFromNumber(column) + row;
    private static string Text(IXLCell cell) => cell.GetFormattedString().Trim();
    private static string Normalize(string? value) => CollapseWhitespaceRegex().Replace((value ?? string.Empty).Normalize(NormalizationForm.FormKC), " ").Trim().ToUpper(TurkishCulture);
    private static string NormalizeBranch(string? value) => string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
    private static string TrimTo(string value, int length) => value.Trim().Length <= length ? value.Trim() : value.Trim()[..length];

    private static KkdSimpleMatrixWorkbookIssue Issue(string code, string message, string? sheet = null, int? row = null, string? cell = null) =>
        new(code, message, sheet, row, cell);

    private static async Task<byte[]> ReadBytesAsync(Stream source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        await using var target = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            if (total > MaxFileSize) throw AppException.BadRequest("Basit KKD Excel dosyası en fazla 15 MB olabilir.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total == 0) throw AppException.BadRequest("Yüklenecek basit KKD Excel dosyası boş olamaz.");
        return target.ToArray();
    }

    private static XLWorkbook OpenWorkbook(byte[] bytes)
    {
        try { return new XLWorkbook(new MemoryStream(bytes)); }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        { throw AppException.BadRequest("Dosya geçerli bir XLSX çalışma kitabı değil."); }
    }

    [GeneratedRegex(@"(\d+)\s*\.?\s*AY", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthOffsetRegex();

    [GeneratedRegex(@"(\d+)\s*G[ÜU]N", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DayPeriodRegex();

    [GeneratedRegex(@"(\d+)\s*HAFTA", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeekPeriodRegex();

    [GeneratedRegex(@"(\d+)\s*AY(?:DA|LIK)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonthPeriodRegex();

    [GeneratedRegex(@"(\d+)\s*YIL", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearPeriodRegex();

    [GeneratedRegex(@"\b\d{3}(?:-\d{2,4}){3,4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex StockCodeRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex CollapseWhitespaceRegex();

    private sealed record StockReference(long Id, string Code, string Name, string? GroupCode);
    private sealed record TemplatePhase(int Column, string PhaseKey, string Label);
    private sealed record TemplateBlock(string RuleKey, string Name, string? Standard, string Identifier, IReadOnlyList<TemplatePhase> Phases);
    private sealed record PhaseColumn(
        int Column,
        string Header,
        string PhaseType,
        int OffsetMonths,
        string? PeriodType,
        int? PeriodInterval,
        int? FrequencyDays,
        decimal? QuantityPerFrequency);
    private sealed record ProductBlock(
        int StartColumn,
        int EndColumn,
        string Name,
        string? Standard,
        string Identifier,
        string? GroupCode,
        StockReference? Stock,
        string RuleIdentity,
        IReadOnlyList<PhaseColumn> Phases);
    private sealed record SourceRow(
        int Row,
        string DepartmentText,
        string RoleText,
        string WorkerClass,
        KkdDepartment? Department,
        KkdRole? Role,
        IReadOnlyDictionary<int, decimal?> Values);
    private sealed record MatrixDraft(long? ExistingId, KkdMatrixUpsertRequest Request);
    private sealed record PreparedImport(
        string FileHash,
        string StateHash,
        int SourceRowCount,
        IReadOnlyList<MatrixDraft> Drafts,
        IReadOnlyList<KkdSimpleMatrixWorkbookIssue> Errors,
        IReadOnlyList<KkdSimpleMatrixWorkbookIssue> Warnings,
        int DuplicateRowCount,
        int RuleCount,
        int PhaseCount,
        int CreateCount);
}
