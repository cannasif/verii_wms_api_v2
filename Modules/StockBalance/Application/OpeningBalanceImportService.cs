using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.StockBalance.Application;

public sealed class OpeningBalanceImportService(
    IUnitOfWork unitOfWork,
    IStockMovementService stockMovements) : IOpeningBalanceImportService
{
    public const int MaxRows = 200;
    public const int WarehouseOpeningMaxRows = 50_000;
    public const int MaxFileSize = 5 * 1024 * 1024;
    public const int WarehouseOpeningMaxFileSize = 64 * 1024 * 1024;
    public const int MovementBatchSize = 500;
    private const int QueryBatchSize = 1_000;
    private const int LastTemplateRow = MaxRows + 1;
    private static readonly string[] Headers =
    [
        "WarehouseCode", "LocationCode", "StockCode", "YapCode", "Quantity", "UnitCode",
        "LotNo", "SerialNo", "StockStatus", "OccurredAt", "Description"
    ];
    private static readonly IReadOnlySet<string> StockStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "Available", "QualityHold", "Quarantine", "Rejected" };

    public async Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken cancellationToken = default)
    {
        var branch = NormalizeBranch(branchCode);
        var warehouses = await unitOfWork.Repository<WarehouseEntity>().Query()
            .Where(x => x.BranchCode == branch).OrderBy(x => x.WarehouseCode)
            .Select(x => new { x.Id, x.WarehouseCode, x.WarehouseName }).ToListAsync(cancellationToken);
        var warehouseIds = warehouses.Select(x => x.Id).ToList();
        var locationRows = await unitOfWork.Repository<WarehouseLocation>().Query()
            .Where(x => warehouseIds.Contains(x.WarehouseId) && x.IsActive)
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.Code)
            .Select(x => new { x.WarehouseId, x.Code, x.Name, x.LocationType }).ToListAsync(cancellationToken);
        var stocks = await unitOfWork.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branch).OrderBy(x => x.ErpStockCode)
            .Select(x => new { x.ErpStockCode, x.StockName, x.BaseUnitCode }).Take(5000).ToListAsync(cancellationToken);
        var yapCodes = await unitOfWork.Repository<YapCodeEntity>().Query()
            .Where(x => x.BranchCode == branch).OrderBy(x => x.ConfigurationCode)
            .Select(x => new { x.ConfigurationCode, x.Description }).Take(5000).ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("İlk Raf Bakiyeleri");
        WriteHeaders(sheet, Headers);
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, LastTemplateRow, Headers.Length).SetAutoFilter();
        sheet.Range(2, 1, LastTemplateRow, Headers.Length).Style.Border
            .SetBottomBorder(XLBorderStyleValues.Hair).Border.SetBottomBorderColor(XLColor.FromHtml("#D8E1EA"));
        sheet.Columns(1, Headers.Length).Width = 20;
        sheet.Column(11).Width = 42;
        sheet.Range(2, 5, LastTemplateRow, 5).Style.NumberFormat.Format = "#,##0.0000";
        sheet.Range(2, 10, LastTemplateRow, 10).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        AddList(sheet.Range(2, 9, LastTemplateRow, 9), string.Join(",", StockStatuses));
        AddUnique(sheet.Range(2, 8, LastTemplateRow, 8),
            $"OR(H2=\"\",COUNTIFS($C$2:$C${LastTemplateRow},C2,$H$2:$H${LastTemplateRow},H2)=1)",
            "Aynı stok ve seri numarası dosyada yalnız bir kez bulunabilir.");

        var guide = workbook.Worksheets.Add("Açıklamalar");
        guide.Range("A1:F1").Merge();
        guide.Cell("A1").Value = "WMS V2 — İlk Raf Bakiyesi Aktarımı";
        guide.Range("A1:F1").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#10243E"))
            .Font.SetFontColor(XLColor.White).Font.SetBold().Font.SetFontSize(16);
        guide.Range("A3:F5").Merge();
        guide.Cell("A3").Value =
            "Bu işlem raf bakiyesi tablosuna doğrudan yazmaz; denetlenebilir bir ilk bakiye stok hareketi oluşturur. " +
            "Her hedef deponun hareket defteri tamamen boş olmalıdır. Aynı idempotency anahtarıyla güvenli biçimde tekrar denenebilir. " +
            "Mevcut bakiye güncellenmez veya silinmez; sonraki düzeltmeler normal stok düzeltme işlemiyle yapılmalıdır. " +
            "'Stoklar' ve 'YAP Kodları' sayfaları yalnızca ilk 5.000 kaydı örnek referans olarak gösterir. Bu listelerde görünmeyen " +
            "geçerli kodlar da ana sayfaya yazılabilir ve yükleme sırasında doğrudan veritabanından doğrulanır.";
        guide.Range("A3:F5").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF4D6"))
            .Font.SetFontColor(XLColor.FromHtml("#7A4B00")).Font.SetBold().Alignment.SetWrapText();
        var notes = new[]
        {
            new[] { "Alan", "Zorunlu", "Kural", "Örnek", "Kaynak", "Not" },
            new[] { "WarehouseCode", "Evet", "Şubedeki ve hiç stok hareketi olmayan depo", "1", "Depolar", "İlk yükleme koruması uygulanır" },
            new[] { "LocationCode", "Evet", "Depoya ait aktif raf/lokasyon", "A01-R01-G01", "Aktif Raflar", "WarehouseCode ile birlikte eşleşir" },
            new[] { "StockCode", "Evet", "Şubedeki aktif stok kodu", "01/001", "Stoklar", "Netsis stok kodudur" },
            new[] { "YapCode", "Hayır", "Stokla uyumlu yapılandırma kodu", "YAP-001", "YAP Kodları", "YAP kullanılmıyorsa boş" },
            new[] { "Quantity", "Evet", "Sıfırdan büyük", "25,5", "", "Seri politikasına göre miktar=1 gerekebilir" },
            new[] { "UnitCode", "Hayır", "Boşsa stok ana birimi", "ADET", "Stoklar", "Stok birim politikası doğrular" },
            new[] { "LotNo / SerialNo", "Politikaya göre", "Stok takip kuralına uygun", "LOT-01 / SR-001", "", "Seri tekilliği hareket servisi tarafından kontrol edilir" },
            new[] { "StockStatus", "Evet", "Available | QualityHold | Quarantine | Rejected", "Available", "", "Başlangıçtaki gerçek statü" },
            new[] { "OccurredAt", "Hayır", "Tüm satırlarda aynı tarih/saat", "2026-07-29 09:00", "", "Boşsa aktarım zamanı" }
        };
        for (var r = 0; r < notes.Length; r++)
            for (var c = 0; c < notes[r].Length; c++)
                guide.Cell(r + 7, c + 1).Value = notes[r][c];
        StyleTable(guide, 7, notes.Length + 6, 6);
        guide.Columns(1, 6).AdjustToContents(1, notes.Length + 6, 12, 46);
        guide.SheetView.FreezeRows(6);

        var reference = workbook.Worksheets.Add("Aktif Raflar");
        WriteHeaders(reference, ["WarehouseCode", "WarehouseName", "LocationCode", "LocationName", "LocationType"]);
        var warehouseById = warehouses.ToDictionary(x => x.Id);
        for (var i = 0; i < locationRows.Count; i++)
        {
            var warehouse = warehouseById[locationRows[i].WarehouseId];
            reference.Cell(i + 2, 1).Value = warehouse.WarehouseCode;
            reference.Cell(i + 2, 2).Value = warehouse.WarehouseName;
            reference.Cell(i + 2, 3).Value = locationRows[i].Code;
            reference.Cell(i + 2, 4).Value = locationRows[i].Name;
            reference.Cell(i + 2, 5).Value = locationRows[i].LocationType;
        }
        if (locationRows.Count > 0) StyleTable(reference, 1, locationRows.Count + 1, 5);
        reference.Columns(1, 5).AdjustToContents(1, Math.Max(2, locationRows.Count + 1), 12, 38);

        var stockSheet = workbook.Worksheets.Add("Stoklar");
        WriteHeaders(stockSheet, ["StockCode", "StockName", "BaseUnitCode"]);
        for (var i = 0; i < stocks.Count; i++)
        {
            stockSheet.Cell(i + 2, 1).Value = stocks[i].ErpStockCode;
            stockSheet.Cell(i + 2, 2).Value = stocks[i].StockName;
            stockSheet.Cell(i + 2, 3).Value = stocks[i].BaseUnitCode;
        }
        if (stocks.Count > 0) StyleTable(stockSheet, 1, stocks.Count + 1, 3);
        stockSheet.Columns(1, 3).AdjustToContents(1, Math.Max(2, stocks.Count + 1), 12, 42);

        var yapSheet = workbook.Worksheets.Add("YAP Kodları");
        WriteHeaders(yapSheet, ["YapCode", "Description"]);
        for (var i = 0; i < yapCodes.Count; i++)
        {
            yapSheet.Cell(i + 2, 1).Value = yapCodes[i].ConfigurationCode;
            yapSheet.Cell(i + 2, 2).Value = yapCodes[i].Description;
        }
        if (yapCodes.Count > 0) StyleTable(yapSheet, 1, yapCodes.Count + 1, 2);
        yapSheet.Columns(1, 2).AdjustToContents(1, Math.Max(2, yapCodes.Count + 1), 12, 42);

        var example = workbook.Worksheets.Add("Örnek");
        WriteHeaders(example, Headers);
        if (warehouses.Count > 0 && locationRows.Count > 0 && stocks.Count > 0)
        {
            var loc = locationRows.First();
            var wh = warehouseById[loc.WarehouseId];
            object?[] values = [wh.WarehouseCode, loc.Code, stocks[0].ErpStockCode, null, 10m,
                stocks[0].BaseUnitCode, null, null, "Available", DateTime.Today, "Devir açılış bakiyesi"];
            for (var c = 0; c < values.Length; c++) SetValue(example.Cell(2, c + 1), values[c]);
        }
        example.Columns(1, Headers.Length).AdjustToContents(1, 2, 12, 34);

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public Task<OpeningBalanceImportResult> ImportAsync(
        Stream workbookStream,
        string branchCode,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        => ImportCoreAsync(
            workbookStream, branchCode, idempotencyKey, MaxRows, MaxFileSize, false, cancellationToken);

    public Task<OpeningBalanceImportResult> ImportWarehouseOpeningAsync(
        Stream workbookStream,
        string branchCode,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        => ImportCoreAsync(
            workbookStream,
            branchCode,
            idempotencyKey,
            WarehouseOpeningMaxRows,
            WarehouseOpeningMaxFileSize,
            false,
            cancellationToken);

    public async Task<OpeningBalanceImportValidation> ValidateWarehouseOpeningAsync(
        Stream workbookStream,
        string branchCode,
        CancellationToken cancellationToken = default)
    {
        var result = await ImportCoreAsync(
            workbookStream,
            branchCode,
            $"preview-{Guid.NewGuid():N}",
            WarehouseOpeningMaxRows,
            WarehouseOpeningMaxFileSize,
            true,
            cancellationToken);
        return new(result.TotalRows, result.TotalQuantity, result.BatchCount);
    }

    private async Task<OpeningBalanceImportResult> ImportCoreAsync(
        Stream workbookStream,
        string branchCode,
        string idempotencyKey,
        int maxRows,
        int maxFileSize,
        bool validateOnly,
        CancellationToken cancellationToken)
    {
        var branch = NormalizeBranch(branchCode);
        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is < 8 or > 100) throw AppException.BadRequest("İdempotency anahtarı 8-100 karakter olmalıdır.");
        await using var buffered = await BufferAsync(workbookStream, maxFileSize, cancellationToken);
        using var workbook = OpenWorkbook(buffered);
        var worksheet = workbook.Worksheets.FirstOrDefault(x => x.Name == "İlk Raf Bakiyeleri")
            ?? throw AppException.BadRequest("'İlk Raf Bakiyeleri' çalışma sayfası bulunamadı.");
        ValidateHeaders(worksheet);
        var rows = worksheet.RowsUsed()
            .Where(x => x.RowNumber() > 1 && Enumerable.Range(1, Headers.Length).Any(c => !string.IsNullOrWhiteSpace(x.Cell(c).GetString())))
            .Take(maxRows + 1).ToList();
        if (rows.Count == 0) throw AppException.BadRequest("Aktarılacak ilk bakiye satırı bulunamadı.");
        if (rows.Count > maxRows) throw AppException.BadRequest($"Tek aktarımda en fazla {maxRows} bakiye satırı kullanılabilir.");

        var requestedWarehouseCodes = rows
            .Select(x => Text(x, 1))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var requestedWarehouseNumbers = requestedWarehouseCodes
            .Select(x => int.TryParse(x, out var value) ? (int?)value : null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var requestedLocationCodes = rows
            .Select(x => Text(x, 2).ToUpperInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var requestedStockCodes = rows
            .Select(x => Text(x, 3))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var requestedYapCodes = rows
            .Select(x => Null(Text(x, 4)))
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var warehouses = await unitOfWork.Repository<WarehouseEntity>().Query()
            .Where(x => x.BranchCode == branch
                && requestedWarehouseNumbers.Contains(x.WarehouseCode))
            .Select(x => new { x.Id, x.WarehouseCode })
            .ToListAsync(cancellationToken);
        var warehouseByCode = warehouses.ToDictionary(x => x.WarehouseCode.ToString(), StringComparer.OrdinalIgnoreCase);
        var warehouseIds = warehouses.Select(x => x.Id).ToList();
        var locations = await LoadInBatchesAsync(
            requestedLocationCodes,
            codes => unitOfWork.Repository<WarehouseLocation>().Query()
                .Where(x => warehouseIds.Contains(x.WarehouseId)
                    && x.IsActive
                    && codes.Contains(x.Code))
                .Select(x => new { x.Id, x.WarehouseId, x.Code }),
            cancellationToken);
        var locationByKey = locations.ToDictionary(x => $"{x.WarehouseId}|{x.Code}", StringComparer.OrdinalIgnoreCase);
        var stocks = await LoadInBatchesAsync(
            requestedStockCodes,
            codes => unitOfWork.Repository<StockEntity>().Query()
                .Where(x => x.BranchCode == branch && codes.Contains(x.ErpStockCode)),
            cancellationToken);
        var stockByCode = stocks.ToDictionary(x => x.ErpStockCode, StringComparer.OrdinalIgnoreCase);
        var yapCodes = await LoadInBatchesAsync(
            requestedYapCodes,
            codes => unitOfWork.Repository<YapCodeEntity>().Query()
                .Where(x => x.BranchCode == branch && codes.Contains(x.ConfigurationCode)),
            cancellationToken);
        var yapByCode = yapCodes.GroupBy(x => x.ConfigurationCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var requestLines = new List<StockMovementLineRequest>(rows.Count);
        var resultRows = new List<OpeningBalanceImportRowResult>(rows.Count);
        DateTime? occurredAt = null;
        foreach (var row in rows)
        {
            var warehouseCode = Text(row, 1);
            var locationCode = Text(row, 2).ToUpperInvariant();
            var stockCode = Text(row, 3);
            try
            {
                if (!warehouseByCode.TryGetValue(warehouseCode, out var warehouse))
                    throw AppException.BadRequest($"'{warehouseCode}' depo kodu bu şubede bulunamadı.");
                if (!locationByKey.TryGetValue($"{warehouse.Id}|{locationCode}", out var location))
                    throw AppException.BadRequest($"'{locationCode}' aktif rafı {warehouseCode} deposunda bulunamadı.");
                if (!stockByCode.TryGetValue(stockCode, out var stock))
                    throw AppException.BadRequest($"'{stockCode}' stok kodu bu şubede bulunamadı.");
                var quantity = RequiredDecimal(row, 5);
                if (quantity <= 0) throw AppException.BadRequest("Quantity sıfırdan büyük olmalıdır.");
                var rawStatus = Text(row, 9);
                var status = StockStatuses.FirstOrDefault(x => string.Equals(x, rawStatus, StringComparison.OrdinalIgnoreCase))
                    ?? throw AppException.BadRequest("StockStatus geçersiz.");
                long? yapCodeId = null;
                var yapCode = Null(Text(row, 4));
                if (yapCode is not null)
                {
                    if (!yapByCode.TryGetValue(yapCode, out var yap))
                        throw AppException.BadRequest($"'{yapCode}' YAP kodu bu şubede bulunamadı.");
                    if (yap.StockId.HasValue && yap.StockId != stock.Id)
                        throw AppException.BadRequest($"'{yapCode}' YAP kodu '{stockCode}' stoğuna ait değil.");
                    yapCodeId = yap.Id;
                }
                var rowDate = OptionalDate(row, 10);
                if (rowDate.HasValue)
                {
                    var normalized = rowDate.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(rowDate.Value, DateTimeKind.Local).ToUniversalTime()
                        : rowDate.Value.ToUniversalTime();
                    if (occurredAt.HasValue && occurredAt.Value != normalized)
                        throw AppException.BadRequest("Tüm satırlarda OccurredAt aynı olmalıdır.");
                    occurredAt = normalized;
                }
                requestLines.Add(new(stock.Id, yapCodeId, quantity, null, null, warehouse.Id, location.Id,
                    Null(Text(row, 6)), Null(Text(row, 7)), Null(Text(row, 8)), status));
                resultRows.Add(new(row.RowNumber(), "Ready", warehouseCode, locationCode, stockCode, "Doğrulandı."));
            }
            catch (AppException exception)
            {
                throw AppException.BadRequest($"Satır {row.RowNumber()}: {exception.Message}");
            }
        }

        var duplicateSerial = requestLines.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo))
            .GroupBy(x => $"{x.StockId}|{x.SerialNo}", StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicateSerial is not null) throw AppException.BadRequest($"Aynı stok/seri dosyada tekrar ediyor: {duplicateSerial.First().SerialNo}");

        // Hash normalized business data, not XLSX bytes. ClosedXML package metadata can
        // change while the visible workbook remains identical, which would break resume.
        var fileHash = BuildLogicalFileHash(branch, occurredAt, requestLines, rows);
        var referenceNo = $"OPENING:{fileHash}";
        var description = string.Join(" | ", rows.Select(x => Null(Text(x, 11)))
            .Where(x => x is not null).Distinct().Take(5));
        var postRequests = requestLines.Chunk(MovementBatchSize)
            .Select((lines, index) => new PostStockMovementRequest(
                BuildBatchKey(branch, fileHash, index),
                StockMovementTypes.AdjustmentIncrease,
                "OpeningBalanceImport",
                referenceNo,
                null,
                occurredAt,
                "İlk raf bakiyesi aktarımı",
                description,
                lines))
            .ToList();

        // A retry of the exact same file must be able to resume after a process or
        // network interruption. Already committed chunks are replayed by PostAsync;
        // validating their serials as new inbound stock would incorrectly reject them.
        var batchKeys = postRequests.Select(x => x.IdempotencyKey).ToList();
        var completedBatchKeys = (await unitOfWork.Repository<StockMovementOperation>().Query()
                .Where(x => batchKeys.Contains(x.IdempotencyKey))
                .Select(x => x.IdempotencyKey)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // Validate every not-yet-committed chunk before the first new commit. A bad
        // row must not leave additional partial opening records behind.
        foreach (var request in postRequests.Where(x => !completedBatchKeys.Contains(x.IdempotencyKey)))
            await stockMovements.ValidateAsync(request, cancellationToken);

        if (validateOnly)
            return new(0, Guid.Empty, false, rows.Count, requestLines.Sum(x => x.Quantity), [],
                postRequests.Count, rows.Count > 500);

        var operations = unitOfWork.Repository<StockMovementOperation>().Query();
        var targetWarehouseIds = requestLines.Select(x => x.TargetWarehouseId!.Value).Distinct().ToList();
        var foreignWarehouseId = await unitOfWork.Repository<StockMovementEntry>().Query()
            .Where(entry => targetWarehouseIds.Contains(entry.WarehouseId)
                && !operations.Any(operation => operation.Id == entry.OperationId
                    && operation.ReferenceType == "OpeningBalanceImport"
                    && operation.ReferenceNo == referenceNo))
            .Select(entry => (long?)entry.WarehouseId)
            .FirstOrDefaultAsync(cancellationToken);
        if (foreignWarehouseId.HasValue)
        {
            var code = warehouses.First(x => x.Id == foreignWarehouseId.Value).WarehouseCode;
            throw AppException.Conflict(
                $"{code} deposunda bu dosyadan bağımsız stok hareketi mevcut. İlk bakiye yalnız hareket defteri boş depoya aktarılabilir.");
        }

        var postedBatches = new List<StockMovementPostResult>(postRequests.Count);
        foreach (var request in postRequests)
        {
            postedBatches.Add(await stockMovements.PostAsync(request, cancellationToken));
            unitOfWork.ClearTracking();
        }

        var allReplayed = postedBatches.All(x => x.IsReplay);
        const int resultRowLimit = 500;
        var finalRows = resultRows.Take(resultRowLimit).Select(x => x with
        {
            Status = allReplayed ? "Replayed" : "Posted",
            Message = allReplayed ? "Önceki ilk bakiye işlemi güvenli biçimde yeniden döndürüldü." : "İlk bakiye hareketine kaydedildi."
        }).ToList();
        var first = postedBatches[0];
        return new(first.OperationId, first.OperationCode, allReplayed, rows.Count,
            requestLines.Sum(x => x.Quantity), finalRows, postedBatches.Count,
            resultRows.Count > resultRowLimit);
    }

    private static string BuildBatchKey(string branch, string fileHash, int index) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"warehouse-opening|{branch}|{fileHash}|batch-size:{MovementBatchSize}|{index:D6}")));

    private static string BuildLogicalFileHash(
        string branch,
        DateTime? occurredAt,
        IReadOnlyList<StockMovementLineRequest> lines,
        IReadOnlyList<IXLRow> rows)
    {
        var payload = new
        {
            Branch = branch,
            OccurredAt = occurredAt,
            Lines = lines.Select((line, index) => new
            {
                line.StockId,
                line.YapCodeId,
                line.Quantity,
                line.TargetWarehouseId,
                line.TargetLocationId,
                UnitCode = line.UnitCode?.Trim().ToUpperInvariant(),
                LotNo = line.LotNo?.Trim(),
                SerialNo = line.SerialNo?.Trim().ToUpperInvariant(),
                StockStatus = line.StockStatus?.Trim(),
                Description = Null(Text(rows[index], 11))
            }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }

    private static async Task<List<TEntity>> LoadInBatchesAsync<TKey, TEntity>(
        IReadOnlyCollection<TKey> keys,
        Func<TKey[], IQueryable<TEntity>> query,
        CancellationToken cancellationToken)
    {
        var result = new List<TEntity>();
        foreach (var batch in keys.Chunk(QueryBatchSize))
            result.AddRange(await query(batch).ToListAsync(cancellationToken));
        return result;
    }

    private static string Text(IXLRow row, int column) => row.Cell(column).GetString().Trim();
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal RequiredDecimal(IXLRow row, int column) => row.Cell(column).TryGetValue<decimal>(out var value)
        ? value : decimal.TryParse(Text(row, column), out value) ? value
        : throw AppException.BadRequest($"{Headers[column - 1]} sayısal olmalıdır.");
    private static DateTime? OptionalDate(IXLRow row, int column)
    {
        if (string.IsNullOrWhiteSpace(Text(row, column))) return null;
        if (row.Cell(column).TryGetValue<DateTime>(out var date)) return date;
        return DateTime.TryParse(Text(row, column), out date) ? date
            : throw AppException.BadRequest($"{Headers[column - 1]} geçerli tarih/saat olmalıdır.");
    }
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
    private static async Task<MemoryStream> BufferAsync(Stream source, int maxFileSize, CancellationToken ct)
    {
        var target = new MemoryStream(); var buffer = new byte[81920]; var total = 0;
        while (true) { var read = await source.ReadAsync(buffer, ct); if (read == 0) break; total += read;
            if (total > maxFileSize) { await target.DisposeAsync(); throw AppException.BadRequest($"XLSX dosyası en fazla {maxFileSize / 1024 / 1024} MB olabilir."); }
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
    private static void AddUnique(IXLRange range, string formula, string message) { var v = range.CreateDataValidation(); v.Custom(formula); v.IgnoreBlanks = true; v.ShowErrorMessage = true; v.ErrorStyle = XLErrorStyle.Stop; v.ErrorTitle = "Tekrarlanan seri"; v.ErrorMessage = message; }
    private static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: return;
            case string text: cell.Value = text; break;
            case int integer: cell.Value = integer; break;
            case decimal number: cell.Value = number; break;
            case DateTime date: cell.Value = date; break;
            default: cell.Value = value.ToString() ?? string.Empty; break;
        }
    }
}
