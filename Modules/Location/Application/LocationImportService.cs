using System.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Location.Application;

public sealed class LocationImportService(IUnitOfWork unitOfWork, ILocationService locations) : ILocationImportService
{
    public const int MaxRows = 1000;
    public const int MaxFileSize = 5 * 1024 * 1024;
    private const int LastTemplateRow = MaxRows + 1;
    private static readonly string[] Headers =
    [
        "WarehouseCode", "LocationCode", "LocationName", "LocationType", "ParentLocationCode",
        "BarcodeEntryMode", "Barcode", "ZoneCode", "AisleNo", "RackNo", "LevelNo", "BinNo",
        "CapacityQuantity", "CapacityWeight", "CapacityVolume", "CapacityUnit",
        "AllowMixedStock", "AllowMixedLot", "AllowMixedStatus", "AllowCycleCount",
        "IsPickable", "IsPutaway", "IsQuarantine", "IsActive", "Description"
    ];

    public async Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken cancellationToken = default)
    {
        var branch = NormalizeBranch(branchCode);
        var warehouses = await unitOfWork.Repository<WarehouseEntity>().Query()
            .Where(x => x.BranchCode == branch)
            .OrderBy(x => x.WarehouseCode)
            .Select(x => new { x.WarehouseCode, x.WarehouseName })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Raf Tanımları");
        WriteHeaders(sheet, Headers);
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, LastTemplateRow, Headers.Length).SetAutoFilter();
        sheet.Range(2, 1, LastTemplateRow, Headers.Length).Style.Border
            .SetBottomBorder(XLBorderStyleValues.Hair).Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));
        sheet.Columns(1, Headers.Length).Width = 18;
        sheet.Column(3).Width = 30;
        sheet.Column(25).Width = 40;
        AddList(sheet.Range(2, 4, LastTemplateRow, 4), string.Join(",", LocationTypes.All));
        AddList(sheet.Range(2, 6, LastTemplateRow, 6), string.Join(",", BarcodeEntryModes.All));
        foreach (var column in Enumerable.Range(17, 8))
            AddList(sheet.Range(2, column, LastTemplateRow, column), "true,false");
        AddUnique(sheet.Range(2, 2, LastTemplateRow, 2),
            $"COUNTIFS($A$2:$A${LastTemplateRow},A2,$B$2:$B${LastTemplateRow},B2)=1",
            "Aynı depo içinde raf kodu dosyada yalnız bir kez bulunabilir.");

        var guide = workbook.Worksheets.Add("Açıklamalar");
        guide.Range("A1:F1").Merge();
        guide.Cell("A1").Value = "WMS V2 — İlk Raf Tanımı Aktarımı";
        guide.Range("A1:F1").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(16);
        guide.Range("A3:F4").Merge();
        guide.Cell("A3").Value =
            "Bu aktarım yalnız yeni raf/lokasyon tanımı oluşturur; mevcut kayıtları güncellemez, pasife almaz veya silmez. " +
            "Hiyerarşik satırlar dosyada herhangi bir sırada olabilir. ParentLocationCode aynı depodaki bir kayıt olmalıdır.";
        guide.Range("A3:F4").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF4D6"))
            .Font.SetFontColor(XLColor.FromHtml("#7A4B00")).Font.SetBold().Alignment.SetWrapText();
        var notes = new[]
        {
            new[] { "Alan", "Zorunlu", "Kural", "Örnek", "Not", "Davranış" },
            new[] { "WarehouseCode", "Evet", "Şubedeki depo kodu", "1", "Depolar sayfasından", "Kodla eşleştirilir" },
            new[] { "LocationCode", "Evet", "A-Z, 0-9, nokta, alt çizgi veya tire; en fazla 50", "A01-R01-G01", "Depo içinde tekil", "Büyük harfe çevrilir" },
            new[] { "LocationType", "Evet", string.Join(" | ", LocationTypes.All), "Cell", "Listeden seçin", "Hiyerarşi doğrulanır" },
            new[] { "ParentLocationCode", "Tipe göre", "Aynı depodaki üst lokasyon", "A01-R01", "Zone kök olmalıdır", "Dosyadaki üst satır da olabilir" },
            new[] { "BarcodeEntryMode", "Evet", "Auto | Manual", "Auto", "Manual ise Barcode zorunlu", "Auto barkodu sistem üretir" },
            new[] { "Kapasite alanları", "Hayır", "Negatif olamaz", "1000 / KG", "Değer varsa CapacityUnit zorunlu", "Raf kapasitesine yazılır" },
            new[] { "Boolean alanlar", "Evet", "true | false", "true", "Listeden seçin", "1/0 ve evet/hayır yüklemede kabul edilir" }
        };
        for (var r = 0; r < notes.Length; r++)
            for (var c = 0; c < notes[r].Length; c++)
                guide.Cell(r + 6, c + 1).Value = notes[r][c];
        StyleTable(guide, 6, notes.Length + 5, 6);
        guide.Columns(1, 6).AdjustToContents(1, notes.Length + 5, 12, 44);
        guide.SheetView.FreezeRows(5);

        var warehouseSheet = workbook.Worksheets.Add("Depolar");
        WriteHeaders(warehouseSheet, ["WarehouseCode", "WarehouseName"]);
        for (var i = 0; i < warehouses.Count; i++)
        {
            warehouseSheet.Cell(i + 2, 1).Value = warehouses[i].WarehouseCode;
            warehouseSheet.Cell(i + 2, 2).Value = warehouses[i].WarehouseName;
        }
        if (warehouses.Count > 0) StyleTable(warehouseSheet, 1, warehouses.Count + 1, 2);
        warehouseSheet.Columns(1, 2).AdjustToContents(1, Math.Max(2, warehouses.Count + 1), 12, 40);

        var example = workbook.Worksheets.Add("Örnek");
        WriteHeaders(example, Headers);
        var warehouseCode = warehouses.FirstOrDefault()?.WarehouseCode ?? 1;
        WriteExample(example, 2, warehouseCode, "A01", "A Koridoru", "Zone", null);
        WriteExample(example, 3, warehouseCode, "A01-R01", "A Koridoru Raf 1", "Rack", "A01");
        WriteExample(example, 4, warehouseCode, "A01-R01-G01", "A Koridoru Raf 1 Göz 1", "Cell", "A01-R01");
        example.Columns(1, Headers.Length).AdjustToContents(1, 4, 12, 30);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<LocationImportResult> ImportAsync(Stream workbookStream, string branchCode,
        CancellationToken cancellationToken = default)
    {
        var branch = NormalizeBranch(branchCode);
        await using var buffered = await BufferAsync(workbookStream, cancellationToken);
        using var workbook = OpenWorkbook(buffered);
        var worksheet = workbook.Worksheets.FirstOrDefault(x => x.Name == "Raf Tanımları")
            ?? throw AppException.BadRequest("'Raf Tanımları' çalışma sayfası bulunamadı.");
        ValidateHeaders(worksheet);
        var sourceRows = worksheet.RowsUsed()
            .Where(x => x.RowNumber() > 1 && Enumerable.Range(1, Headers.Length).Any(c => !string.IsNullOrWhiteSpace(x.Cell(c).GetString())))
            .Take(MaxRows + 1).ToList();
        if (sourceRows.Count == 0) throw AppException.BadRequest("Aktarılacak raf satırı bulunamadı.");
        if (sourceRows.Count > MaxRows) throw AppException.BadRequest($"En fazla {MaxRows} raf satırı aktarılabilir.");

        var warehouseRows = await unitOfWork.Repository<WarehouseEntity>().Query()
            .Where(x => x.BranchCode == branch).Select(x => new { x.Id, x.WarehouseCode }).ToListAsync(cancellationToken);
        var warehouseByCode = warehouseRows.ToDictionary(x => x.WarehouseCode.ToString(), x => x.Id, StringComparer.OrdinalIgnoreCase);
        var existing = await unitOfWork.Repository<WarehouseLocation>().Query()
            .Where(x => warehouseRows.Select(w => w.Id).Contains(x.WarehouseId))
            .Select(x => new { x.Id, x.WarehouseId, x.Code }).ToListAsync(cancellationToken);
        var known = existing.ToDictionary(x => Key(x.WarehouseId, x.Code), x => x.Id, StringComparer.OrdinalIgnoreCase);
        var parsed = sourceRows.Select(Parse).ToList();
        var duplicate = parsed.GroupBy(x => $"{x.WarehouseCode}|{x.Code}", StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw AppException.BadRequest($"Satır {duplicate.First().RowNumber}: Aynı depo ve raf kodu dosyada tekrar ediyor.");
        foreach (var row in parsed)
        {
            if (!warehouseByCode.TryGetValue(row.WarehouseCode, out var warehouseId))
                throw AppException.BadRequest($"Satır {row.RowNumber}: '{row.WarehouseCode}' depo kodu bu şubede bulunamadı.");
            row.WarehouseId = warehouseId;
            if (known.ContainsKey(Key(row.WarehouseId, row.Code)))
                throw AppException.Conflict($"Satır {row.RowNumber}: '{row.Code}' rafı bu depoda zaten mevcut; ilk aktarım mevcut kayıtları değiştirmez.");
        }

        var pending = parsed.ToList();
        var results = new List<LocationImportRowResult>(parsed.Count);
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            while (pending.Count > 0)
            {
                var creatable = pending.Where(x => string.IsNullOrWhiteSpace(x.ParentCode) || known.ContainsKey(Key(x.WarehouseId, x.ParentCode))).ToList();
                if (creatable.Count == 0)
                    throw AppException.BadRequest($"Satır {pending[0].RowNumber}: Üst raf '{pending[0].ParentCode}' bulunamadı veya hiyerarşide döngü var.");
                foreach (var row in creatable)
                {
                    long? parentId = string.IsNullOrWhiteSpace(row.ParentCode) ? null : known[Key(row.WarehouseId, row.ParentCode)];
                    try
                    {
                        var id = await locations.CreateAsync(new(row.WarehouseId, parentId, row.Code, row.Name, row.LocationType,
                            row.BarcodeMode, row.Barcode, row.ZoneCode, row.AisleNo, row.RackNo, row.LevelNo, row.BinNo,
                            row.CapacityQuantity, row.CapacityWeight, row.CapacityVolume, row.CapacityUnit,
                            row.AllowMixedStock, row.AllowMixedLot, row.AllowMixedStatus, row.AllowCycleCount,
                            row.IsPickable, row.IsPutaway, row.IsQuarantine, row.IsActive, row.Description), ct);
                        known[Key(row.WarehouseId, row.Code)] = id;
                        results.Add(new(row.RowNumber, "Created", row.WarehouseCode, row.Code, "Raf oluşturuldu."));
                        pending.Remove(row);
                    }
                    catch (AppException exception)
                    {
                        throw AppException.BadRequest($"Satır {row.RowNumber}: {exception.Message}");
                    }
                }
            }
            return true;
        }, cancellationToken, IsolationLevel.Serializable);
        return new(parsed.Count, results.Count, 0, results.OrderBy(x => x.RowNumber).ToList());
    }

    private static ParsedLocation Parse(IXLRow row)
    {
        var locationCode = Text(row, 2).ToUpperInvariant();
        return new(row.RowNumber(), Text(row, 1), locationCode, Text(row, 3), Text(row, 4),
            Null(Text(row, 5))?.ToUpperInvariant(), Text(row, 6), Null(Text(row, 7)), Null(Text(row, 8)),
            Int(row, 9), Int(row, 10), Int(row, 11), Int(row, 12), Decimal(row, 13), Decimal(row, 14), Decimal(row, 15),
            Null(Text(row, 16)), Bool(row, 17), Bool(row, 18), Bool(row, 19), Bool(row, 20),
            Bool(row, 21), Bool(row, 22), Bool(row, 23), Bool(row, 24), Null(Text(row, 25)));
    }

    private static void WriteExample(IXLWorksheet sheet, int row, int warehouse, string code, string name, string type, string? parent)
    {
        object?[] values = [warehouse, code, name, type, parent, "Auto", null, "A", null, null, null, null,
            null, null, null, null, false, false, false, true, true, true, false, true, "Örnek kayıt"];
        for (var c = 0; c < values.Length; c++) if (values[c] is not null) sheet.Cell(row, c + 1).Value = XLCellValue.FromObject(values[c]);
    }
    private static string Key(long warehouseId, string code) => $"{warehouseId}|{code.Trim().ToUpperInvariant()}";
    private static string Text(IXLRow row, int column) => row.Cell(column).GetString().Trim();
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int? Int(IXLRow row, int column) => string.IsNullOrWhiteSpace(Text(row, column)) ? null :
        row.Cell(column).TryGetValue<int>(out var value) ? value : throw AppException.BadRequest($"Satır {row.RowNumber()}: {Headers[column - 1]} tam sayı olmalıdır.");
    private static decimal? Decimal(IXLRow row, int column) => string.IsNullOrWhiteSpace(Text(row, column)) ? null :
        row.Cell(column).TryGetValue<decimal>(out var value) ? value : throw AppException.BadRequest($"Satır {row.RowNumber()}: {Headers[column - 1]} sayısal olmalıdır.");
    private static bool Bool(IXLRow row, int column) => Text(row, column).ToLowerInvariant() switch
    {
        "true" or "1" or "evet" or "yes" => true,
        "false" or "0" or "hayır" or "hayir" or "no" => false,
        _ => throw AppException.BadRequest($"Satır {row.RowNumber()}: {Headers[column - 1]} true veya false olmalıdır.")
    };
    private static void ValidateHeaders(IXLWorksheet sheet)
    {
        var actual = Enumerable.Range(1, Headers.Length).Select(x => sheet.Cell(1, x).GetString().Trim()).ToArray();
        if (!actual.SequenceEqual(Headers, StringComparer.Ordinal))
            throw AppException.BadRequest($"Excel başlıkları veya sırası geçersiz. Beklenen: {string.Join(", ", Headers)}.");
    }
    private static string NormalizeBranch(string? value) => string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
    private static XLWorkbook OpenWorkbook(Stream stream)
    {
        try { return new XLWorkbook(stream); }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        { throw AppException.BadRequest("Dosya geçerli bir XLSX çalışma kitabı değil."); }
    }
    private static async Task<MemoryStream> BufferAsync(Stream source, CancellationToken ct)
    {
        var target = new MemoryStream(); var buffer = new byte[81920]; var total = 0;
        while (true) { var read = await source.ReadAsync(buffer, ct); if (read == 0) break; total += read;
            if (total > MaxFileSize) { await target.DisposeAsync(); throw AppException.BadRequest("XLSX dosyası en fazla 5 MB olabilir."); }
            await target.WriteAsync(buffer.AsMemory(0, read), ct); }
        if (total == 0) { await target.DisposeAsync(); throw AppException.BadRequest("Yüklenecek XLSX dosyası boş olamaz."); }
        target.Position = 0; return target;
    }
    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Range(1, 1, 1, headers.Count).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        sheet.Row(1).Height = 28;
    }
    private static void StyleTable(IXLWorksheet sheet, int firstRow, int lastRow, int columns)
    {
        sheet.Range(firstRow, 1, firstRow, columns).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#0E7490")).Font.SetFontColor(XLColor.White).Font.SetBold();
        if (lastRow > firstRow) sheet.Range(firstRow + 1, 1, lastRow, columns).Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
    }
    private static void AddList(IXLRange range, string values) { var v = range.CreateDataValidation(); v.List($"\"{values}\"", true); v.ShowErrorMessage = true; v.ErrorStyle = XLErrorStyle.Stop; }
    private static void AddUnique(IXLRange range, string formula, string message) { var v = range.CreateDataValidation(); v.Custom(formula); v.IgnoreBlanks = true; v.ShowErrorMessage = true; v.ErrorStyle = XLErrorStyle.Stop; v.ErrorTitle = "Tekrarlanan değer"; v.ErrorMessage = message; }

    private sealed record ParsedLocation(int RowNumber, string WarehouseCode, string Code, string Name,
        string LocationType, string? ParentCode, string BarcodeMode, string? Barcode, string? ZoneCode,
        int? AisleNo, int? RackNo, int? LevelNo, int? BinNo, decimal? CapacityQuantity, decimal? CapacityWeight,
        decimal? CapacityVolume, string? CapacityUnit, bool AllowMixedStock, bool AllowMixedLot, bool AllowMixedStatus,
        bool AllowCycleCount, bool IsPickable, bool IsPutaway, bool IsQuarantine, bool IsActive, string? Description)
    { public long WarehouseId { get; set; } }
}
