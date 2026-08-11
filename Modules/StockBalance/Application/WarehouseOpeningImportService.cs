using System.Data;
using System.Security.Cryptography;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.StockBalance.Application;

public sealed record WarehouseOpeningPreview(
    string FileHash,
    int WarehouseCount,
    int NewLocationCount,
    int ExistingLocationCount,
    int BalanceRowCount,
    int DistinctStockCount,
    int SerialCount,
    decimal TotalQuantity,
    int BatchCount,
    IReadOnlyList<string> Warnings);

public sealed record WarehouseOpeningImportResult(
    string FileHash,
    LocationImportResult? Locations,
    OpeningBalanceImportResult Balances);

public interface IWarehouseOpeningImportService
{
    Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken cancellationToken = default);
    Task<WarehouseOpeningPreview> PreviewAsync(
        Stream workbookStream,
        string branchCode,
        CancellationToken cancellationToken = default);
    Task<WarehouseOpeningImportResult> ImportAsync(
        Stream workbookStream,
        string branchCode,
        string previewHash,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class WarehouseOpeningImportService(
    IUnitOfWork unitOfWork,
    ILocationImportService locationImport,
    IOpeningBalanceImportService openingBalanceImport) : IWarehouseOpeningImportService
{
    public const int MaxRows = 50_000;
    public const int MaxFileSize = 64 * 1024 * 1024;
    private const int LastTemplateRow = MaxRows + 1;
    private static readonly string[] Headers =
    [
        "WarehouseCode", "LocationCode", "LocationName", "LocationType", "ParentLocationCode",
        "Barcode", "ZoneCode", "AisleNo", "RackNo", "LevelNo", "BinNo",
        "IsPickable", "IsPutaway", "IsQuarantine",
        "StockCode", "YapCode", "Quantity", "UnitCode", "LotNo", "SerialNo",
        "StockStatus", "OccurredAt", "Description"
    ];
    private static readonly string[] LocationHeaders =
    [
        "WarehouseCode", "LocationCode", "LocationName", "LocationType", "ParentLocationCode",
        "BarcodeEntryMode", "Barcode", "ZoneCode", "AisleNo", "RackNo", "LevelNo", "BinNo",
        "CapacityQuantity", "CapacityWeight", "CapacityVolume", "CapacityUnit",
        "AllowMixedStock", "AllowMixedLot", "AllowMixedStatus", "AllowCycleCount",
        "IsPickable", "IsPutaway", "IsQuarantine", "IsActive", "Description"
    ];
    private static readonly string[] BalanceHeaders =
    [
        "WarehouseCode", "LocationCode", "StockCode", "YapCode", "Quantity", "UnitCode",
        "LotNo", "SerialNo", "StockStatus", "OccurredAt", "Description"
    ];

    public async Task<byte[]> CreateTemplateAsync(
        string branchCode,
        CancellationToken cancellationToken = default)
    {
        var locationBytes = await locationImport.CreateTemplateAsync(branchCode, cancellationToken);
        var balanceBytes = await openingBalanceImport.CreateTemplateAsync(branchCode, cancellationToken);
        using var locationWorkbook = new XLWorkbook(new MemoryStream(locationBytes));
        using var balanceWorkbook = new XLWorkbook(new MemoryStream(balanceBytes));
        using var workbook = new XLWorkbook();

        var main = workbook.Worksheets.Add("Depo Açılışları");
        WriteHeaders(main, Headers);
        main.SheetView.FreezeRows(1);
        main.Range(1, 1, LastTemplateRow, Headers.Length).SetAutoFilter();
        main.Columns(1, Headers.Length).Width = 18;
        main.Column(3).Width = 28;
        main.Column(23).Width = 40;
        AddList(main.Range(2, 4, LastTemplateRow, 4), string.Join(",", LocationTypes.All));
        foreach (var column in new[] { 12, 13, 14 })
            AddList(main.Range(2, column, LastTemplateRow, column), "true,false");
        AddList(main.Range(2, 21, LastTemplateRow, 21), "Available,QualityHold,Quarantine,Rejected");
        main.Range(2, 17, LastTemplateRow, 17).Style.NumberFormat.Format = "#,##0.0000";
        main.Range(2, 22, LastTemplateRow, 22).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";

        var guide = workbook.Worksheets.Add("Açıklamalar");
        guide.Range("A1:F1").Merge();
        guide.Cell("A1").Value = "WMS V2 — Tek Dosya Depo Açılış Aktarımı";
        guide.Range("A1:F1").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(16);
        guide.Range("A3:F6").Merge();
        guide.Cell("A3").Value =
            "Her satır depo + raf + stok + miktar/lot/seri bilgisidir. Aynı depo ve raf istenildiği kadar tekrar edebilir; " +
            "sistem WarehouseCode + LocationCode ile tekilleştirir. Raf mevcutsa kullanılır, yoksa LocationName ve LocationType " +
            "ile bir kez oluşturulur. StockCode boş bırakılan satırlar yalnız üst/ara raf tanımı oluşturur. Ön doğrulama başarılı " +
            "olmadan kayıt yapılmaz. Büyük dosyalar 500 hareket satırlık idempotent partilerle kaydedilir; kesinti olursa aynı dosya güvenle devam ettirilir.";
        guide.Range("A3:F6").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF4D6"))
            .Font.SetFontColor(XLColor.FromHtml("#7A4B00")).Font.SetBold().Alignment.SetWrapText();
        var notes = new[]
        {
            new[] { "Alan", "Zorunlu", "Tekillik/Kural", "Örnek", "Davranış", "Not" },
            new[] { "WarehouseCode + LocationCode", "Evet", "Raf anahtarı", "1 / A01-R01-G01", "Tek raf olarak çözülür", "100 stok/seri satırında tekrar edebilir" },
            new[] { "LocationName + LocationType", "Yeni rafta", "Aynı raf tekrarlarında çelişemez", "Göz 1 / Cell", "Raf yoksa oluşturulur", "Mevcut rafta boş kalabilir" },
            new[] { "ParentLocationCode", "Hiyerarşiye göre", "Aynı depoda bulunmalı", "A01-R01", "Üst raf önce çözülür", "StockCode boş satırla tanımlanabilir" },
            new[] { "StockCode", "Bakiye satırında", "Şubedeki stok", "STK-01", "Açılış hareketine eklenir", "Boşsa yalnız raf tanımıdır" },
            new[] { "Quantity", "Bakiye satırında", "> 0", "25,5", "Raf bakiyesine hareketle yansır", "Doğrudan bakiye tablosuna yazılmaz" },
            new[] { "LotNo / SerialNo", "Stok kuralına göre", "Stok+seri dosyada tekil", "LOT-01 / SR-001", "Hareket satırına yazılır", "Aynı raf farklı serilerle tekrarlanabilir" },
            new[] { "StockStatus", "Bakiye satırında", "Listeden", "Available", "Başlangıç statüsüdür", "Kalite/Karantina desteklenir" }
        };
        for (var row = 0; row < notes.Length; row++)
            for (var column = 0; column < notes[row].Length; column++)
                guide.Cell(row + 8, column + 1).Value = notes[row][column];
        StyleTable(guide, 8, notes.Length + 7, 6);
        guide.Columns(1, 6).AdjustToContents(1, notes.Length + 7, 12, 48);

        CopyValues(locationWorkbook.Worksheet("Depolar"), workbook.Worksheets.Add("Depolar"));
        CopyValues(balanceWorkbook.Worksheet("Aktif Raflar"), workbook.Worksheets.Add("Mevcut Aktif Raflar"));
        CopyValues(balanceWorkbook.Worksheet("Stoklar"), workbook.Worksheets.Add("Stoklar"));
        CopyValues(balanceWorkbook.Worksheet("YAP Kodları"), workbook.Worksheets.Add("YAP Kodları"));

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<WarehouseOpeningPreview> PreviewAsync(
        Stream workbookStream,
        string branchCode,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(workbookStream, branchCode, cancellationToken);
        await using var balanceStream = new MemoryStream(prepared.BalanceWorkbook);
        var validation = await openingBalanceImport.ValidateWarehouseOpeningAsync(
            balanceStream, branchCode, cancellationToken);
        return ToPreview(prepared, validation.TotalQuantity, validation.BatchCount);
    }

    public async Task<WarehouseOpeningImportResult> ImportAsync(
        Stream workbookStream,
        string branchCode,
        string previewHash,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(workbookStream, branchCode, cancellationToken);
        if (!string.Equals(prepared.FileHash, previewHash?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict(
                "Yüklenecek dosya ön doğrulaması yapılan dosyayla aynı değil. Dosyayı yeniden ön doğrulayın.");

        return await ExecutePreparedAsync(prepared, branchCode, idempotencyKey, cancellationToken);
    }

    private async Task<PreparedWorkbook> PrepareAsync(
        Stream source,
        string branchCode,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(source, cancellationToken);
        using var workbook = OpenWorkbook(bytes);
        var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name == "Depo Açılışları")
            ?? throw AppException.BadRequest("'Depo Açılışları' çalışma sayfası bulunamadı.");
        ValidateHeaders(sheet);
        var usedRows = sheet.RowsUsed()
            .Where(x => x.RowNumber() > 1
                && Enumerable.Range(1, Headers.Length).Any(c => !string.IsNullOrWhiteSpace(x.Cell(c).GetString())))
            .Take(MaxRows + 1)
            .ToList();
        if (usedRows.Count == 0) throw AppException.BadRequest("Aktarılacak depo açılış satırı bulunamadı.");
        if (usedRows.Count > MaxRows) throw AppException.BadRequest($"En fazla {MaxRows} satır aktarılabilir.");

        var rows = usedRows.Select(Parse).ToList();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.WarehouseCode))
                throw AppException.BadRequest($"Satır {row.RowNumber}: WarehouseCode zorunludur.");
            if (string.IsNullOrWhiteSpace(row.LocationCode))
                throw AppException.BadRequest($"Satır {row.RowNumber}: LocationCode zorunludur.");
        }
        var branch = string.IsNullOrWhiteSpace(branchCode) ? "0" : branchCode.Trim();
        var warehouses = await unitOfWork.Repository<WarehouseEntity>().Query()
            .Where(x => x.BranchCode == branch)
            .Select(x => new { x.Id, x.WarehouseCode })
            .ToListAsync(cancellationToken);
        var warehouseByCode = warehouses.ToDictionary(
            x => x.WarehouseCode.ToString(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            if (!warehouseByCode.ContainsKey(row.WarehouseCode))
                throw AppException.BadRequest(
                    $"Satır {row.RowNumber}: '{row.WarehouseCode}' depo kodu giriş yapılan şubede bulunamadı.");

        var warehouseIds = warehouses.Select(x => x.Id).ToList();
        var existing = await unitOfWork.Repository<WarehouseLocation>().Query()
            .Where(x => warehouseIds.Contains(x.WarehouseId))
            .Select(x => new { x.WarehouseId, x.Code })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(x => Key(x.WarehouseId, x.Code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groups = rows.GroupBy(
            x => $"{x.WarehouseCode}|{x.LocationCode}",
            StringComparer.OrdinalIgnoreCase);
        var newLocations = new List<FlatRow>();
        var existingLocationCount = 0;
        foreach (var group in groups)
        {
            var first = group.First();
            var warehouseId = warehouseByCode[first.WarehouseCode].Id;
            if (existingKeys.Contains(Key(warehouseId, first.LocationCode)))
            {
                existingLocationCount++;
                continue;
            }

            ValidateConsistent(group, x => x.LocationName, "LocationName");
            ValidateConsistent(group, x => x.LocationType, "LocationType");
            ValidateConsistent(group, x => x.ParentLocationCode, "ParentLocationCode");
            ValidateConsistent(group, x => x.Barcode, "Barcode");
            ValidateConsistent(group, x => x.ZoneCode, "ZoneCode");
            ValidateConsistent(group, x => x.AisleNo, "AisleNo");
            ValidateConsistent(group, x => x.RackNo, "RackNo");
            ValidateConsistent(group, x => x.LevelNo, "LevelNo");
            ValidateConsistent(group, x => x.BinNo, "BinNo");
            ValidateConsistent(group, x => x.IsPickable, "IsPickable");
            ValidateConsistent(group, x => x.IsPutaway, "IsPutaway");
            ValidateConsistent(group, x => x.IsQuarantine, "IsQuarantine");
            var definition = first with
            {
                LocationName = FirstValue(group, x => x.LocationName),
                LocationType = FirstValue(group, x => x.LocationType),
                ParentLocationCode = FirstValue(group, x => x.ParentLocationCode),
                Barcode = FirstValue(group, x => x.Barcode),
                ZoneCode = FirstValue(group, x => x.ZoneCode),
                AisleNo = FirstValue(group, x => x.AisleNo),
                RackNo = FirstValue(group, x => x.RackNo),
                LevelNo = FirstValue(group, x => x.LevelNo),
                BinNo = FirstValue(group, x => x.BinNo),
                IsPickable = FirstValue(group, x => x.IsPickable),
                IsPutaway = FirstValue(group, x => x.IsPutaway),
                IsQuarantine = FirstValue(group, x => x.IsQuarantine)
            };
            if (string.IsNullOrWhiteSpace(definition.LocationName)
                || string.IsNullOrWhiteSpace(definition.LocationType))
                throw AppException.BadRequest(
                    $"Satır {first.RowNumber}: Yeni '{first.LocationCode}' rafı için LocationName ve LocationType zorunludur.");
            newLocations.Add(definition);
        }

        var balanceRows = rows.Where(x => !string.IsNullOrWhiteSpace(x.StockCode)).ToList();
        if (balanceRows.Count == 0)
            throw AppException.BadRequest("En az bir stok bakiyesi satırı bulunmalıdır.");

        return new(
            Convert.ToHexString(SHA256.HashData(bytes)),
            rows,
            newLocations,
            existingLocationCount,
            BuildLocationWorkbook(newLocations),
            BuildBalanceWorkbook(balanceRows));
    }

    private async Task<WarehouseOpeningImportResult> ExecutePreparedAsync(
        PreparedWorkbook prepared,
        string branchCode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        LocationImportResult? locationResult = null;
        if (prepared.NewLocations.Count > 0)
        {
            await using var locationStream = new MemoryStream(prepared.LocationWorkbook);
            locationResult = await locationImport.ImportAsync(
                locationStream, branchCode, cancellationToken);
        }

        await using var balanceStream = new MemoryStream(prepared.BalanceWorkbook);
        var balanceResult = await openingBalanceImport.ImportWarehouseOpeningAsync(
            balanceStream, branchCode, idempotencyKey, cancellationToken);
        return new(prepared.FileHash, locationResult, balanceResult);
    }

    private static WarehouseOpeningPreview ToPreview(
        PreparedWorkbook prepared,
        decimal totalQuantity,
        int batchCount)
    {
        var balanceRows = prepared.Rows.Where(x => !string.IsNullOrWhiteSpace(x.StockCode)).ToList();
        var warnings = new List<string>();
        if (prepared.ExistingLocationCount > 0)
            warnings.Add($"{prepared.ExistingLocationCount} raf zaten mevcut; yeniden oluşturulmadan kullanılacak.");
        return new(
            prepared.FileHash,
            prepared.Rows.Select(x => x.WarehouseCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            prepared.NewLocations.Count,
            prepared.ExistingLocationCount,
            balanceRows.Count,
            balanceRows.Select(x => x.StockCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            balanceRows.Count(x => !string.IsNullOrWhiteSpace(x.SerialNo)),
            totalQuantity,
            batchCount,
            warnings);
    }

    private static byte[] BuildLocationWorkbook(IReadOnlyList<FlatRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Raf Tanımları");
        WriteHeaders(sheet, LocationHeaders);
        for (var index = 0; index < rows.Count; index++)
        {
            var source = rows[index];
            object?[] values =
            [
                source.WarehouseCode, source.LocationCode, source.LocationName, source.LocationType,
                source.ParentLocationCode, string.IsNullOrWhiteSpace(source.Barcode) ? "Auto" : "Manual",
                source.Barcode, source.ZoneCode, source.AisleNo, source.RackNo, source.LevelNo, source.BinNo,
                null, null, null, null, false, false, false, true,
                source.IsPickable ?? true, source.IsPutaway ?? true, source.IsQuarantine ?? false, true, source.Description
            ];
            WriteRow(sheet, index + 2, values);
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildBalanceWorkbook(IReadOnlyList<FlatRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("İlk Raf Bakiyeleri");
        WriteHeaders(sheet, BalanceHeaders);
        for (var index = 0; index < rows.Count; index++)
        {
            var source = rows[index];
            object?[] values =
            [
                source.WarehouseCode, source.LocationCode, source.StockCode, source.YapCode,
                source.Quantity, source.UnitCode, source.LotNo, source.SerialNo,
                source.StockStatus, source.OccurredAt, source.Description
            ];
            WriteRow(sheet, index + 2, values);
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static FlatRow Parse(IXLRow row) => new(
        row.RowNumber(),
        Text(row, 1),
        Text(row, 2).ToUpperInvariant(),
        Null(Text(row, 3)),
        Null(Text(row, 4)),
        Null(Text(row, 5))?.ToUpperInvariant(),
        Null(Text(row, 6)),
        Null(Text(row, 7)),
        Integer(row, 8),
        Integer(row, 9),
        Integer(row, 10),
        Integer(row, 11),
        Boolean(row, 12),
        Boolean(row, 13),
        Boolean(row, 14),
        Null(Text(row, 15)),
        Null(Text(row, 16)),
        Decimal(row, 17),
        Null(Text(row, 18)),
        Null(Text(row, 19)),
        Null(Text(row, 20)),
        Null(Text(row, 21)) ?? "Available",
        Null(Text(row, 22)),
        Null(Text(row, 23)));

    private static void ValidateConsistent(
        IEnumerable<FlatRow> rows,
        Func<FlatRow, string?> selector,
        string field)
    {
        var values = rows.Select(selector).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (values.Count > 1)
            throw AppException.BadRequest(
                $"Aynı depo/raf tekrarlarında {field} değerleri çelişemez: {string.Join(", ", values)}.");
    }

    private static void ValidateConsistent(
        IEnumerable<FlatRow> rows,
        Func<FlatRow, int?> selector,
        string field)
    {
        var values = rows.Select(selector).Where(x => x.HasValue).Select(x => x!.Value)
            .Distinct().ToList();
        if (values.Count > 1)
            throw AppException.BadRequest(
                $"Aynı depo/raf tekrarlarında {field} değerleri çelişemez: {string.Join(", ", values)}.");
    }

    private static void ValidateConsistent(
        IEnumerable<FlatRow> rows,
        Func<FlatRow, bool?> selector,
        string field)
    {
        var values = rows.Select(selector).Where(x => x.HasValue).Select(x => x!.Value)
            .Distinct().ToList();
        if (values.Count > 1)
            throw AppException.BadRequest(
                $"Aynı depo/raf tekrarlarında {field} değerleri çelişemez: {string.Join(", ", values)}.");
    }

    private static string? FirstValue(
        IEnumerable<FlatRow> rows,
        Func<FlatRow, string?> selector) =>
        rows.Select(selector).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static int? FirstValue(
        IEnumerable<FlatRow> rows,
        Func<FlatRow, int?> selector) =>
        rows.Select(selector).FirstOrDefault(x => x.HasValue);

    private static bool? FirstValue(
        IEnumerable<FlatRow> rows,
        Func<FlatRow, bool?> selector) =>
        rows.Select(selector).FirstOrDefault(x => x.HasValue);

    private static string Key(long warehouseId, string locationCode) =>
        $"{warehouseId}|{locationCode.Trim().ToUpperInvariant()}";
    private static string Text(IXLRow row, int column) => row.Cell(column).GetString().Trim();
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int? Integer(IXLRow row, int column) =>
        string.IsNullOrWhiteSpace(Text(row, column)) ? null
        : row.Cell(column).TryGetValue<int>(out var value) ? value
        : throw AppException.BadRequest($"Satır {row.RowNumber()}: {Headers[column - 1]} tam sayı olmalıdır.");
    private static decimal? Decimal(IXLRow row, int column) =>
        string.IsNullOrWhiteSpace(Text(row, column)) ? null
        : row.Cell(column).TryGetValue<decimal>(out var value) ? value
        : decimal.TryParse(Text(row, column), out value) ? value
        : throw AppException.BadRequest($"Satır {row.RowNumber()}: {Headers[column - 1]} sayısal olmalıdır.");
    private static bool? Boolean(IXLRow row, int column) =>
        string.IsNullOrWhiteSpace(Text(row, column)) ? null : Text(row, column).ToLowerInvariant() switch
        {
            "true" or "1" or "evet" or "yes" => true,
            "false" or "0" or "hayır" or "hayir" or "no" => false,
            _ => throw AppException.BadRequest(
                $"Satır {row.RowNumber()}: {Headers[column - 1]} true veya false olmalıdır.")
        };

    private static void ValidateHeaders(IXLWorksheet sheet)
    {
        var actual = Enumerable.Range(1, Headers.Length)
            .Select(x => sheet.Cell(1, x).GetString().Trim()).ToArray();
        if (!actual.SequenceEqual(Headers, StringComparer.Ordinal))
            throw AppException.BadRequest(
                $"Excel başlıkları veya sırası geçersiz. Beklenen: {string.Join(", ", Headers)}.");
    }

    private static async Task<byte[]> ReadBytesAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var target = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaxFileSize)
                throw AppException.BadRequest("XLSX dosyası en fazla 8 MB olabilir.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total == 0) throw AppException.BadRequest("Yüklenecek XLSX dosyası boş olamaz.");
        return target.ToArray();
    }

    private static XLWorkbook OpenWorkbook(byte[] bytes)
    {
        try { return new XLWorkbook(new MemoryStream(bytes)); }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        { throw AppException.BadRequest("Dosya geçerli bir XLSX çalışma kitabı değil."); }
    }

    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
            sheet.Cell(1, index + 1).Value = headers[index];
        sheet.Range(1, 1, 1, headers.Count).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold();
    }

    private static void WriteRow(IXLWorksheet sheet, int row, IReadOnlyList<object?> values)
    {
        for (var index = 0; index < values.Count; index++)
            if (values[index] is not null)
                sheet.Cell(row, index + 1).Value = XLCellValue.FromObject(values[index]);
    }

    private static void CopyValues(IXLWorksheet source, IXLWorksheet target)
    {
        var range = source.RangeUsed();
        if (range is null) return;
        foreach (var cell in range.CellsUsed())
            target.Cell(cell.Address.RowNumber, cell.Address.ColumnNumber).Value = cell.Value;
        target.Columns(1, range.ColumnCount()).AdjustToContents(1, range.RowCount(), 12, 44);
    }

    private static void StyleTable(IXLWorksheet sheet, int firstRow, int lastRow, int columns)
    {
        sheet.Range(firstRow, 1, firstRow, columns).Style.Fill
            .SetBackgroundColor(XLColor.FromHtml("#0E7490")).Font.SetFontColor(XLColor.White).Font.SetBold();
        if (lastRow > firstRow)
            sheet.Range(firstRow + 1, 1, lastRow, columns).Style.Border
                .SetBottomBorder(XLBorderStyleValues.Thin);
    }

    private static void AddList(IXLRange range, string values)
    {
        var validation = range.CreateDataValidation();
        validation.List($"\"{values}\"", true);
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Stop;
    }

    private sealed record FlatRow(
        int RowNumber,
        string WarehouseCode,
        string LocationCode,
        string? LocationName,
        string? LocationType,
        string? ParentLocationCode,
        string? Barcode,
        string? ZoneCode,
        int? AisleNo,
        int? RackNo,
        int? LevelNo,
        int? BinNo,
        bool? IsPickable,
        bool? IsPutaway,
        bool? IsQuarantine,
        string? StockCode,
        string? YapCode,
        decimal? Quantity,
        string? UnitCode,
        string? LotNo,
        string? SerialNo,
        string StockStatus,
        string? OccurredAt,
        string? Description);

    private sealed record PreparedWorkbook(
        string FileHash,
        IReadOnlyList<FlatRow> Rows,
        IReadOnlyList<FlatRow> NewLocations,
        int ExistingLocationCount,
        byte[] LocationWorkbook,
        byte[] BalanceWorkbook);
}
