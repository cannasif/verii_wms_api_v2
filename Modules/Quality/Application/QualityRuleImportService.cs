using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Quality.Application;

public sealed class QualityRuleImportService(
    IUnitOfWork uow,
    IQualityService qualityService) : IQualityRuleImportService
{
    public const int MaxImportRows = 1000;
    public const int MaxImportFileSize = 5 * 1024 * 1024;
    private const int LastTemplateRow = MaxImportRows + 1;

    private static readonly string[] Headers =
    [
        "ScopeType", "StockCode", "StockGroupCode", "InspectionMode", "SamplingMode",
        "SamplingValue", "FailAction", "AutoQuarantine", "RequireLot", "RequireSerial",
        "RequireExpiryDate", "MinimumRemainingShelfLifeDays", "IsActive", "Description"
    ];

    public async Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var groups = await uow.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branch && x.GroupCode != null && x.GroupCode != "")
            .GroupBy(x => x.GroupCode!.Trim().ToUpper())
            .Select(x => new { Code = x.Key, StockCount = x.Count() })
            .OrderBy(x => x.Code)
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var rules = workbook.Worksheets.Add("Kalite Kuralları");
        WriteHeaders(rules, Headers);
        rules.SheetView.FreezeRows(1);
        rules.Range(1, 1, LastTemplateRow, Headers.Length).SetAutoFilter();
        rules.Columns(1, Headers.Length).Width = 20;
        rules.Column(14).Width = 42;
        rules.Range(2, 1, LastTemplateRow, Headers.Length).Style
            .Border.SetBottomBorder(XLBorderStyleValues.Hair)
            .Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));

        AddListValidation(rules.Range(2, 1, LastTemplateRow, 1), "Stock,StockGroup");
        AddListValidation(rules.Range(2, 4, LastTemplateRow, 4), "NoCheck,QuickCheck,InspectionRequired");
        AddListValidation(rules.Range(2, 5, LastTemplateRow, 5), "All,Percentage,FixedQuantity,EveryNthHandlingUnit");
        AddListValidation(rules.Range(2, 7, LastTemplateRow, 7), "Quarantine,Reject,ReturnToSupplier,ManagerApproval");
        foreach (var column in new[] { 8, 9, 10, 11, 13 })
            AddListValidation(rules.Range(2, column, LastTemplateRow, column), "true,false");

        var guide = workbook.Worksheets.Add("Açıklamalar");
        guide.Range("A1:F1").Merge();
        guide.Cell("A1").Value = "WMS V2 — Toplu Kalite Kuralı Aktarımı";
        guide.Range("A1:F1").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(16);
        guide.Range("A3:F4").Merge();
        guide.Cell("A3").Value =
            "Aktarım yalnızca yeni kalite kuralları oluşturur. Mevcut kurallar güncellenmez veya silinmez; " +
            "aynı aktif stok/stok grubu kapsamı varsa satır değiştirilmeden atlanır. Düzenleme işlemi kalite kuralı ekranından yapılır.";
        guide.Range("A3:F4").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF4D6"))
            .Font.SetFontColor(XLColor.FromHtml("#7A4B00")).Font.SetBold().Alignment.SetWrapText();
        var guideRows = new[]
        {
            new[] { "Alan", "Zorunlu", "Kural", "Stock örneği", "StockGroup örneği", "Not" },
            new[] { "ScopeType", "Evet", "Stock veya StockGroup", "Stock", "StockGroup", "Listeden seçin" },
            new[] { "StockCode", "Stock için", "Şubedeki mevcut stok kodu", "01/001", "", "StockGroup satırında boş" },
            new[] { "StockGroupCode", "StockGroup için", "Stok Grupları sayfasındaki kod", "", "SAC", "Stock satırında boş" },
            new[] { "InspectionMode", "Evet", "NoCheck | QuickCheck | InspectionRequired", "InspectionRequired", "QuickCheck", "" },
            new[] { "SamplingMode", "Evet", "All | Percentage | FixedQuantity | EveryNthHandlingUnit", "All", "Percentage", "" },
            new[] { "SamplingValue", "Evet", "Sıfırdan büyük; Percentage için en fazla 100", "100", "10", "" },
            new[] { "FailAction", "Evet", "Quarantine | Reject | ReturnToSupplier | ManagerApproval", "Quarantine", "ManagerApproval", "" },
            new[] { "Boolean alanlar", "Evet", "true veya false", "true", "false", "Listeden seçin" },
            new[] { "MinimumRemainingShelfLifeDays", "Hayır", "Sıfır veya pozitif tam sayı", "", "30", "" },
            new[] { "Description", "Hayır", "En fazla 500 karakter", "Giriş kalite kontrolü", "Grup kuralı", "" }
        };
        for (var row = 0; row < guideRows.Length; row++)
            for (var column = 0; column < guideRows[row].Length; column++)
                guide.Cell(row + 6, column + 1).Value = guideRows[row][column];
        WriteTableStyle(guide, 6, guideRows.Length + 5, 6);
        guide.Columns(1, 6).AdjustToContents(1, guideRows.Length + 5, 12, 42);
        guide.SheetView.FreezeRows(5);

        var groupSheet = workbook.Worksheets.Add("Stok Grupları");
        WriteHeaders(groupSheet, ["StokGroupCode", "Stok Sayısı"]);
        for (var index = 0; index < groups.Count; index++)
        {
            groupSheet.Cell(index + 2, 1).Value = groups[index].Code;
            groupSheet.Cell(index + 2, 2).Value = groups[index].StockCount;
        }
        if (groups.Count > 0) WriteTableStyle(groupSheet, 1, groups.Count + 1, 2);
        groupSheet.Columns(1, 2).AdjustToContents(1, Math.Max(2, groups.Count + 1), 12, 32);
        groupSheet.SheetView.FreezeRows(1);

        var example = workbook.Worksheets.Add("Örnek");
        WriteHeaders(example, Headers);
        var sample = new object?[]
        {
            "StockGroup", null, groups.FirstOrDefault()?.Code ?? "SAC", "InspectionRequired", "All",
            100, "Quarantine", true, false, false, false, null, true, "Örnek grup kalite kuralı"
        };
        for (var column = 0; column < sample.Length; column++)
        {
            if (sample[column] is string text) example.Cell(2, column + 1).Value = text;
            else if (sample[column] is int integer) example.Cell(2, column + 1).Value = integer;
            else if (sample[column] is bool boolean) example.Cell(2, column + 1).Value = boolean;
        }
        example.Columns(1, Headers.Length).AdjustToContents(1, 2, 12, 30);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<QualityRuleImportResult> ImportAsync(
        Stream workbookStream, string branchCode, long actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workbookStream);
        var branch = NormalizeBranch(branchCode);
        await using var buffered = await BufferAsync(workbookStream, ct);
        using var workbook = OpenWorkbook(buffered);
        var worksheet = workbook.Worksheets.FirstOrDefault(x => x.Name == "Kalite Kuralları")
            ?? throw AppException.BadRequest("'Kalite Kuralları' çalışma sayfası bulunamadı.");
        ValidateHeaders(worksheet);
        var rows = worksheet.RowsUsed()
            .Where(x => x.RowNumber() > 1 && HasData(x))
            .Take(MaxImportRows + 1)
            .ToList();
        if (rows.Count > MaxImportRows)
            throw AppException.BadRequest($"Excel dosyası en fazla {MaxImportRows} veri satırı içerebilir.");

        var stocks = await uow.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branch)
            .Select(x => new { x.Id, x.ErpStockCode, x.GroupCode })
            .ToListAsync(ct);
        var stockByCode = stocks
            .GroupBy(x => x.ErpStockCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.OrdinalIgnoreCase);
        var groupCodes = stocks.Where(x => !string.IsNullOrWhiteSpace(x.GroupCode))
            .Select(x => x.GroupCode!.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeRules = await uow.Repository<QualityRule>().Query()
            .Where(x => x.BranchCode == branch && x.IsActive)
            .Select(x => new { x.ScopeType, x.StockId, x.StockGroupCode })
            .ToListAsync(ct);
        var knownScopes = activeRules
            .Select(x => ScopeKey(x.ScopeType, x.StockId, x.StockGroupCode))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<QualityRuleImportRowResult>(rows.Count);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var rawScope = CellText(row, 1);
            var rawStock = CellText(row, 2);
            var rawGroup = CellText(row, 3).ToUpperInvariant();
            var scopeCode = rawScope.Equals(QualityRuleScopeTypes.Stock, StringComparison.OrdinalIgnoreCase)
                ? rawStock : rawGroup;
            try
            {
                var scope = ParseScope(rawScope);
                long? stockId = null;
                string? groupCode = null;
                if (scope == QualityRuleScopeTypes.Stock)
                {
                    if (string.IsNullOrWhiteSpace(rawStock) || !stockByCode.TryGetValue(rawStock, out var resolvedStockId))
                        throw AppException.BadRequest($"'{rawStock}' stok kodu şubede bulunamadı.");
                    stockId = resolvedStockId;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(rawGroup) || !groupCodes.Contains(rawGroup))
                        throw AppException.BadRequest($"'{rawGroup}' stok grubu şubede bulunamadı.");
                    groupCode = rawGroup;
                }

                var key = ScopeKey(scope, stockId, groupCode);
                if (knownScopes.Contains(key))
                {
                    results.Add(new(row.RowNumber(), "Skipped", scope, scopeCode,
                        "Aynı kapsam için aktif kalite kuralı zaten mevcut veya dosyada daha önce tanımlandı; kayıt değiştirilmedi."));
                    continue;
                }

                var request = new QualityRuleUpsertRequest(
                    branch, scope, stockId, groupCode,
                    ParseEnum<QualityInspectionMode>(CellText(row, 4), "InspectionMode"),
                    ParseEnum<QualitySamplingMode>(CellText(row, 5), "SamplingMode"),
                    ParseDecimal(row, 6, "SamplingValue"),
                    ParseEnum<QualityFailAction>(CellText(row, 7), "FailAction"),
                    ParseBoolean(CellText(row, 8), "AutoQuarantine"),
                    ParseBoolean(CellText(row, 9), "RequireLot"),
                    ParseBoolean(CellText(row, 10), "RequireSerial"),
                    ParseBoolean(CellText(row, 11), "RequireExpiryDate"),
                    ParseOptionalInt(CellText(row, 12), "MinimumRemainingShelfLifeDays"),
                    ParseBoolean(CellText(row, 13), "IsActive"),
                    NullIfEmpty(CellText(row, 14)));
                await qualityService.CreateRuleAsync(request, actor, ct);
                knownScopes.Add(key);
                results.Add(new(row.RowNumber(), "Created", scope, scopeCode, "Kalite kuralı oluşturuldu."));
            }
            catch (AppException exception)
            {
                results.Add(new(row.RowNumber(), "Failed", NullIfEmpty(rawScope) ?? "-", NullIfEmpty(scopeCode), exception.Message));
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                results.Add(new(row.RowNumber(), "Failed", NullIfEmpty(rawScope) ?? "-", NullIfEmpty(scopeCode),
                    "Satır işlenemedi: " + exception.Message));
            }
        }

        return new(
            results.Count,
            results.Count(x => x.Status == "Created"),
            results.Count(x => x.Status == "Skipped"),
            results.Count(x => x.Status == "Failed"),
            results);
    }

    private static string ParseScope(string value) =>
        QualityRuleScopeTypes.All.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))
        ?? throw AppException.BadRequest("ScopeType alanı Stock veya StockGroup olmalıdır.");

    private static T ParseEnum<T>(string value, string field) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw AppException.BadRequest($"{field} alanı geçersiz.");

    private static decimal ParseDecimal(IXLRow row, int column, string field)
    {
        if (row.Cell(column).TryGetValue<decimal>(out var numeric)) return numeric;
        return decimal.TryParse(CellText(row, column), out var parsed)
            ? parsed
            : throw AppException.BadRequest($"{field} sayısal olmalıdır.");
    }

    private static int? ParseOptionalInt(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out var parsed)
            ? parsed
            : throw AppException.BadRequest($"{field} tam sayı olmalıdır.");
    }

    private static bool ParseBoolean(string value, string field) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "evet" or "yes" => true,
        "false" or "0" or "hayır" or "hayir" or "no" => false,
        _ => throw AppException.BadRequest($"{field} alanı true veya false olmalıdır.")
    };

    private static string ScopeKey(string scopeType, long? stockId, string? groupCode) =>
        scopeType.Equals(QualityRuleScopeTypes.Stock, StringComparison.OrdinalIgnoreCase)
            ? $"STOCK:{stockId}"
            : $"GROUP:{groupCode?.Trim().ToUpperInvariant()}";

    private static void ValidateHeaders(IXLWorksheet worksheet)
    {
        var actual = Enumerable.Range(1, Headers.Length)
            .Select(x => worksheet.Cell(1, x).GetString().Trim())
            .ToArray();
        if (!actual.SequenceEqual(Headers, StringComparer.Ordinal))
            throw AppException.BadRequest($"Excel başlıkları geçersiz. Beklenen sıra: {string.Join(", ", Headers)}.");
    }

    private static bool HasData(IXLRow row) =>
        Enumerable.Range(1, Headers.Length).Any(x => !string.IsNullOrWhiteSpace(row.Cell(x).GetString()));

    private static string CellText(IXLRow row, int column) => row.Cell(column).GetString().Trim();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeBranch(string? value) => string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();

    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
            sheet.Cell(1, index + 1).Value = headers[index];
        sheet.Range(1, 1, 1, headers.Count).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold()
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        sheet.Row(1).Height = 28;
    }

    private static void WriteTableStyle(IXLWorksheet sheet, int firstRow, int lastRow, int columns)
    {
        sheet.Range(firstRow, 1, firstRow, columns).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#0E7490"))
            .Font.SetFontColor(XLColor.White).Font.SetBold();
        if (lastRow > firstRow)
            sheet.Range(firstRow + 1, 1, lastRow, columns).Style
                .Border.SetBottomBorder(XLBorderStyleValues.Thin)
                .Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));
    }

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
                throw AppException.BadRequest("XLSX dosyası en fazla 5 MB olabilir.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total == 0)
        {
            await target.DisposeAsync();
            throw AppException.BadRequest("Yüklenecek XLSX dosyası boş olamaz.");
        }
        target.Position = 0;
        return target;
    }
}
