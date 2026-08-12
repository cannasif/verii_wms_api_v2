using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.StockBalance.Application;

public sealed record WarehouseOpeningPreview(
    string FileHash,
    string BalanceSnapshotHash,
    int WarehouseCount,
    int NewLocationCount,
    int ExistingLocationCount,
    int BalanceRowCount,
    int DistinctStockCount,
    int SerialCount,
    decimal TotalQuantity,
    int BatchCount,
    int ExistingMovementCount,
    int CurrentBalanceRowCount,
    decimal CurrentTotalQuantity,
    int ReservedBalanceRowCount,
    decimal ReservedQuantity,
    bool RequiresBalanceReplacement,
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
        bool replaceExistingBalances,
        string balanceSnapshotHash,
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
    private const string OpeningSheetName = "Depo Açılışları";
    private const string DefaultOpeningZoneCode = "WMS-OPENING-ZONE";
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
    private static readonly string[] CustomerBalanceHeaders =
    [
        "TARIH", "STOKKODU", "STOK_ADI", "SERI_NO", "DEPOKOD", "HUCREKODU", "BAKIYE"
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
        var state = await openingBalanceImport.AnalyzeWarehouseStateAsync(
            prepared.WarehouseIds, cancellationToken);
        if (prepared.NewLocations.Count > 0)
            return ToPreview(prepared, prepared.TotalQuantity, prepared.BatchCount, state);

        await using var balanceStream = new MemoryStream(prepared.BalanceWorkbook);
        var validation = await openingBalanceImport.ValidateWarehouseOpeningAsync(
            balanceStream, branchCode, true, state.SnapshotHash, cancellationToken);
        return ToPreview(prepared, validation.TotalQuantity, validation.BatchCount, state);
    }

    public async Task<WarehouseOpeningImportResult> ImportAsync(
        Stream workbookStream,
        string branchCode,
        string previewHash,
        string idempotencyKey,
        bool replaceExistingBalances,
        string balanceSnapshotHash,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(workbookStream, branchCode, cancellationToken);
        if (!string.Equals(prepared.FileHash, previewHash?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict(
                "Yüklenecek dosya ön doğrulaması yapılan dosyayla aynı değil. Dosyayı yeniden ön doğrulayın.");

        return await ExecutePreparedAsync(
            prepared, branchCode, idempotencyKey, replaceExistingBalances,
            balanceSnapshotHash, cancellationToken);
    }

    private async Task<PreparedWorkbook> PrepareAsync(
        Stream source,
        string branchCode,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(source, cancellationToken);
        using var workbook = OpenWorkbook(bytes);
        var (sheet, format) = ResolveInputSheet(workbook);
        var inputHeaders = format == WorkbookFormat.Standard ? Headers : CustomerBalanceHeaders;
        var usedRows = sheet.RowsUsed()
            .Where(x => x.RowNumber() > 1
                && Enumerable.Range(1, inputHeaders.Length).Any(c => !string.IsNullOrWhiteSpace(x.Cell(c).GetString())))
            .Take(MaxRows + 1)
            .ToList();
        if (usedRows.Count == 0) throw AppException.BadRequest("Aktarılacak depo açılış satırı bulunamadı.");
        if (usedRows.Count > MaxRows) throw AppException.BadRequest($"En fazla {MaxRows} satır aktarılabilir.");

        var parsedRows = usedRows.Select(x => format == WorkbookFormat.Standard ? Parse(x) : ParseCustomerBalance(x)).ToList();
        var normalizedLocationCodeCount = parsedRows.Count(x =>
            !string.Equals(x.LocationCode, NormalizeLocationCode(x.LocationCode, x.RowNumber), StringComparison.Ordinal));
        var rows = parsedRows.Select(NormalizeLocationMetadata).ToList();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.WarehouseCode))
                throw AppException.BadRequest($"Satır {row.RowNumber}: WarehouseCode zorunludur.");
            if (string.IsNullOrWhiteSpace(row.LocationCode))
                throw AppException.BadRequest($"Satır {row.RowNumber}: LocationCode zorunludur.");
        }
        var requestedWarehouseNumbers = rows
            .Select(x => int.TryParse(x.WarehouseCode, out var code) ? (int?)code : null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        List<WarehouseLookupRow> warehouses;
        List<LocationLookupRow> existing;
        using (unitOfWork.BeginBranchScope(null))
        {
            warehouses = await unitOfWork.Repository<WarehouseEntity>().Query()
                .Where(x => requestedWarehouseNumbers.Contains(x.WarehouseCode))
                .Select(x => new WarehouseLookupRow(x.Id, x.WarehouseCode, x.BranchCode))
                .ToListAsync(cancellationToken);
            EnsureWarehouseCodesAreUnique(warehouses);
            var warehouseIds = warehouses.Select(x => x.Id).ToList();
            existing = await unitOfWork.Repository<WarehouseLocation>().Query()
                .Where(x => warehouseIds.Contains(x.WarehouseId))
                .Select(x => new LocationLookupRow(x.WarehouseId, x.Code))
                .ToListAsync(cancellationToken);
        }
        var warehouseByCode = warehouses.ToDictionary(
            x => x.WarehouseCode.ToString(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            if (!warehouseByCode.ContainsKey(row.WarehouseCode))
                throw AppException.BadRequest(
                    $"Satır {row.RowNumber}: '{row.WarehouseCode}' depo kodu WMS depo tanımlarında bulunamadı.");

        var existingKeys = existing.Select(x => Key(x.WarehouseId, x.Code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groups = rows.GroupBy(
            x => $"{x.WarehouseCode}|{x.LocationCode}",
            StringComparer.OrdinalIgnoreCase);
        var newLocations = new List<FlatRow>();
        var supportZoneWarehouseIds = new HashSet<long>();
        var inferredLocationMetadataCount = 0;
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
            var locationType = FirstValue(group, x => x.LocationType);
            var parentLocationCode = FirstValue(group, x => x.ParentLocationCode);
            var inferredFlatHierarchy = string.IsNullOrWhiteSpace(locationType)
                && string.IsNullOrWhiteSpace(parentLocationCode);
            if (inferredFlatHierarchy)
            {
                if (string.Equals(first.LocationCode, DefaultOpeningZoneCode, StringComparison.OrdinalIgnoreCase))
                    throw AppException.BadRequest(
                        $"Satır {first.RowNumber}: '{DefaultOpeningZoneCode}' kodu sistemin otomatik açılış bölgesi için ayrılmıştır.");

                locationType = LocationTypes.Rack;
                parentLocationCode = DefaultOpeningZoneCode;
                inferredLocationMetadataCount++;
                if (!existingKeys.Contains(Key(warehouseId, DefaultOpeningZoneCode))
                    && supportZoneWarehouseIds.Add(warehouseId))
                {
                    newLocations.Add(CreateOpeningZone(first));
                }
            }

            var definition = first with
            {
                LocationName = FirstValue(group, x => x.LocationName) ?? first.LocationCode,
                LocationType = locationType,
                ParentLocationCode = parentLocationCode,
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
        await ValidateBalanceReferencesAsync(balanceRows, warehouseByCode, cancellationToken);
        var totalQuantity = balanceRows.Sum(x => x.Quantity ?? 0);
        var batchCount = balanceRows
            .GroupBy(x => warehouseByCode[x.WarehouseCode].BranchCode, StringComparer.OrdinalIgnoreCase)
            .Sum(x => (int)Math.Ceiling(x.Count() / (decimal)OpeningBalanceImportService.MovementBatchSize));
        if (balanceRows.Count == 0)
            throw AppException.BadRequest("En az bir stok bakiyesi satırı bulunmalıdır.");

        return new(
            Convert.ToHexString(SHA256.HashData(bytes)),
            warehouses.Select(x => x.Id).Distinct().OrderBy(x => x).ToArray(),
            rows,
            newLocations,
            existingLocationCount,
            inferredLocationMetadataCount,
            normalizedLocationCodeCount,
            format == WorkbookFormat.CustomerBalance,
            supportZoneWarehouseIds.Count,
            BuildLocationWorkbook(newLocations),
            BuildBalanceWorkbook(balanceRows),
            totalQuantity,
            batchCount);
    }

    private async Task ValidateBalanceReferencesAsync(
        IReadOnlyList<FlatRow> rows,
        IReadOnlyDictionary<string, WarehouseLookupRow> warehouseByCode,
        CancellationToken cancellationToken)
    {
        foreach (var row in rows)
            if (!row.Quantity.HasValue || row.Quantity.Value <= 0
                || row.Quantity.Value > StockMovementLimits.MaxQuantity)
                throw AppException.BadRequest(
                    $"Satır {row.RowNumber}: Quantity sıfırdan büyük ve en fazla {StockMovementLimits.MaxQuantity:N0} olmalıdır.");

        var branches = warehouseByCode.Values.Select(x => x.BranchCode)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var stockCodes = rows.Select(x => x.StockCode!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var requestedYapCodes = rows.Where(x => !string.IsNullOrWhiteSpace(x.YapCode))
            .Select(x => x.YapCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        List<StockReferenceRow> stocks;
        List<YapReferenceRow> configurations;
        using (unitOfWork.BeginBranchScope(null))
        {
            stocks = await unitOfWork.Repository<StockEntity>().Query()
                .Where(x => branches.Contains(x.BranchCode) && stockCodes.Contains(x.ErpStockCode))
                .Select(x => new StockReferenceRow(x.Id, x.BranchCode, x.ErpStockCode))
                .ToListAsync(cancellationToken);
            configurations = await unitOfWork.Repository<YapCodeEntity>().Query()
                .Where(x => branches.Contains(x.BranchCode) && requestedYapCodes.Contains(x.ConfigurationCode))
                .Select(x => new YapReferenceRow(x.BranchCode, x.ConfigurationCode, x.StockId))
                .ToListAsync(cancellationToken);
        }

        var stockByKey = stocks.ToDictionary(
            x => BranchKey(x.BranchCode, x.StockCode),
            StringComparer.OrdinalIgnoreCase);
        var yapByKey = configurations
            .GroupBy(x => BranchKey(x.BranchCode, x.Code), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var branch = warehouseByCode[row.WarehouseCode].BranchCode;
            if (!stockByKey.TryGetValue(BranchKey(branch, row.StockCode!), out var stock))
                throw AppException.BadRequest(
                    $"Satır {row.RowNumber}: '{row.StockCode}' stok kodu deponun bağlı olduğu '{branch}' şubesinde bulunamadı.");
            if (string.IsNullOrWhiteSpace(row.YapCode)) continue;
            if (!yapByKey.TryGetValue(BranchKey(branch, row.YapCode), out var yap))
                throw AppException.BadRequest(
                    $"Satır {row.RowNumber}: '{row.YapCode}' yapılandırma kodu deponun bağlı olduğu '{branch}' şubesinde bulunamadı.");
            if (yap.StockId.HasValue && yap.StockId.Value != stock.Id)
                throw AppException.BadRequest(
                    $"Satır {row.RowNumber}: '{row.YapCode}' yapılandırma kodu '{row.StockCode}' stoğuna ait değil.");
        }
    }

    private async Task<WarehouseOpeningImportResult> ExecutePreparedAsync(
        PreparedWorkbook prepared,
        string branchCode,
        string idempotencyKey,
        bool replaceExistingBalances,
        string balanceSnapshotHash,
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
            balanceStream, branchCode, idempotencyKey, replaceExistingBalances,
            balanceSnapshotHash, cancellationToken);
        return new(prepared.FileHash, locationResult, balanceResult);
    }

    private static WarehouseOpeningPreview ToPreview(
        PreparedWorkbook prepared,
        decimal totalQuantity,
        int batchCount,
        WarehouseOpeningBalanceState state)
    {
        var balanceRows = prepared.Rows.Where(x => !string.IsNullOrWhiteSpace(x.StockCode)).ToList();
        var warnings = new List<string>();
        if (prepared.ExistingLocationCount > 0)
            warnings.Add($"{prepared.ExistingLocationCount} raf zaten mevcut; yeniden oluşturulmadan kullanılacak.");
        if (prepared.InferredLocationMetadataCount > 0)
            warnings.Add(
                $"{prepared.InferredLocationMetadataCount} yeni rafın adı kodundan, tipi Rack olarak ve üst bölgesi otomatik tamamlandı.");
        if (prepared.SupportZoneCount > 0)
            warnings.Add($"{prepared.SupportZoneCount} depo için '{DefaultOpeningZoneCode}' üst bölgesi oluşturulacak.");
        if (prepared.NormalizedLocationCodeCount > 0)
            warnings.Add($"{prepared.NormalizedLocationCodeCount} raf kodu WMS kod standardına dönüştürüldü.");
        if (prepared.UsedCustomerBalanceFormat)
            warnings.Add("7 kolonlu müşteri bakiye formatı algılandı ve WMS depo açılış formatına dönüştürüldü.");
        var requiresReplacement = state.ExistingMovementCount > 0 || state.CurrentBalanceRowCount > 0;
        if (requiresReplacement)
            warnings.Add(
                "Seçilen depolarda mevcut hareket/bakiye var. Onaylanırsa geçmiş silinmeden yalnız fark hareketleri yazılır; Excel'de bulunmayan mevcut raf bakiyeleri sıfıra getirilir.");
        if (state.ReservedQuantity > 0)
            warnings.Add(
                $"{state.ReservedQuantity:N4} miktar açık emirlere rezerve. Rezervasyonlar kapanmadan bakiye eşitlemesi yapılamaz.");
        return new(
            prepared.FileHash,
            state.SnapshotHash,
            prepared.Rows.Select(x => x.WarehouseCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            prepared.NewLocations.Count,
            prepared.ExistingLocationCount,
            balanceRows.Count,
            balanceRows.Select(x => x.StockCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            balanceRows.Count(x => !string.IsNullOrWhiteSpace(x.SerialNo)),
            totalQuantity,
            batchCount,
            state.ExistingMovementCount,
            state.CurrentBalanceRowCount,
            state.CurrentTotalQuantity,
            state.ReservedBalanceRowCount,
            state.ReservedQuantity,
            requiresReplacement,
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
        Text(row, 2),
        Null(Text(row, 3)),
        Null(Text(row, 4)),
        Null(Text(row, 5)),
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
        OptionalDateText(row, 22),
        Null(Text(row, 23)));

    private static FlatRow ParseCustomerBalance(IXLRow row) => new(
        row.RowNumber(),
        Text(row, 5),
        Text(row, 6),
        Null(Text(row, 6)),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        true,
        true,
        false,
        Null(Text(row, 2)),
        null,
        Decimal(row, 7),
        null,
        null,
        Null(Text(row, 4)),
        "Available",
        OptionalDateText(row, 1),
        string.IsNullOrWhiteSpace(Text(row, 3))
            ? "Müşteri devir açılış bakiyesi"
            : $"Müşteri devir açılış bakiyesi - {Text(row, 3)}");

    private static FlatRow NormalizeLocationMetadata(FlatRow row)
    {
        var originalCode = row.LocationCode.Trim();
        var normalizedCode = NormalizeLocationCode(originalCode, row.RowNumber);
        var locationName = string.IsNullOrWhiteSpace(row.LocationName)
            ? string.Equals(originalCode, normalizedCode, StringComparison.Ordinal) ? null : originalCode
            : row.LocationName.Trim();
        if (!string.IsNullOrWhiteSpace(locationName)
            && (locationName.Length < 2 || IsGeneratedShortLocationName(normalizedCode, locationName)))
            locationName = $"{normalizedCode} Raf";
        return row with
        {
            LocationCode = normalizedCode,
            LocationName = locationName,
            LocationType = NormalizeLocationType(row.LocationType),
            ParentLocationCode = string.IsNullOrWhiteSpace(row.ParentLocationCode)
                ? null
                : NormalizeLocationCode(row.ParentLocationCode, row.RowNumber)
        };
    }

    private static bool IsGeneratedShortLocationName(string locationCode, string locationName)
    {
        if (locationCode.Length >= 2) return false;
        static string Compact(string value) => NormalizeAlias(value)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);
        return string.Equals(
            Compact(locationName),
            $"{Compact(locationCode)}RAF",
            StringComparison.OrdinalIgnoreCase);
    }

    private static FlatRow CreateOpeningZone(FlatRow source) => source with
    {
        LocationCode = DefaultOpeningZoneCode,
        LocationName = "Depo Açılış Bölgesi",
        LocationType = LocationTypes.Zone,
        ParentLocationCode = null,
        Barcode = null,
        ZoneCode = "OPENING",
        AisleNo = null,
        RackNo = null,
        LevelNo = null,
        BinNo = null,
        IsPickable = false,
        IsPutaway = false,
        IsQuarantine = false,
        StockCode = null,
        YapCode = null,
        Quantity = null,
        UnitCode = null,
        LotNo = null,
        SerialNo = null,
        StockStatus = "Available",
        OccurredAt = null,
        Description = "Düz müşteri raf kodları için sistem tarafından oluşturulan üst bölge."
    };

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

    private static string BranchKey(string branchCode, string businessCode) =>
        $"{branchCode.Trim()}|{businessCode.Trim()}";

    private static void EnsureWarehouseCodesAreUnique(IReadOnlyCollection<WarehouseLookupRow> warehouses)
    {
        var ambiguous = warehouses
            .GroupBy(x => x.WarehouseCode)
            .FirstOrDefault(x => x.Select(y => y.BranchCode)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (ambiguous is null) return;
        throw AppException.BadRequest(
            $"'{ambiguous.Key}' depo kodu birden fazla şubede tanımlı. Excel satırının hangi depoya ait olduğu belirlenemiyor.");
    }
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

    private static (IXLWorksheet Sheet, WorkbookFormat Format) ResolveInputSheet(XLWorkbook workbook)
    {
        var namedStandard = workbook.Worksheets.FirstOrDefault(x =>
            string.Equals(x.Name, OpeningSheetName, StringComparison.OrdinalIgnoreCase));
        if (namedStandard is not null && HasHeaders(namedStandard, Headers))
            return (namedStandard, WorkbookFormat.Standard);

        var standard = workbook.Worksheets.FirstOrDefault(x => HasHeaders(x, Headers));
        if (standard is not null) return (standard, WorkbookFormat.Standard);

        var customer = workbook.Worksheets.FirstOrDefault(x => HasHeaders(x, CustomerBalanceHeaders));
        if (customer is not null) return (customer, WorkbookFormat.CustomerBalance);

        throw AppException.BadRequest(
            $"Excel başlıkları geçersiz. WMS şablonu ({string.Join(", ", Headers)}) veya müşteri bakiye formatı ({string.Join(", ", CustomerBalanceHeaders)}) kullanılmalıdır.");
    }

    private static bool HasHeaders(IXLWorksheet sheet, IReadOnlyList<string> expected) =>
        Enumerable.Range(1, expected.Count)
            .Select(x => sheet.Cell(1, x).GetString().Trim())
            .SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);

    private static string? OptionalDateText(IXLRow row, int column)
    {
        var cell = row.Cell(column);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<DateTime>(out var value))
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return Null(cell.GetString());
    }

    private static string? NormalizeLocationType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = NormalizeAlias(value);
        return normalized switch
        {
            "ZONE" or "BOLGE" or "ALAN" => LocationTypes.Zone,
            "AISLE" or "KORIDOR" => LocationTypes.Aisle,
            "RACK" or "RAF" => LocationTypes.Rack,
            "SHELF" or "SEVIYE" or "KAT" => LocationTypes.Shelf,
            "CELL" or "HUCRE" or "GOZ" => LocationTypes.Cell,
            "RECEIVING" or "MAL-KABUL" or "KABUL" => LocationTypes.Receiving,
            "STAGING" or "HAZIRLAMA" or "BEKLEME" => LocationTypes.Staging,
            "SHIPPING" or "SEVK" => LocationTypes.Shipping,
            "QUARANTINE" or "KARANTINA" => LocationTypes.Quarantine,
            "VIRTUAL" or "SANAL" => LocationTypes.Virtual,
            _ => value.Trim()
        };
    }

    private static string NormalizeLocationCode(string value, int rowNumber)
    {
        var normalized = NormalizeAlias(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw AppException.BadRequest($"Satır {rowNumber}: LocationCode geçerli bir kod üretmiyor.");
        if (normalized.Length <= 50) return normalized;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..8];
        return $"{normalized[..41].TrimEnd('-', '.', '_')}-{hash}";
    }

    private static string NormalizeAlias(string value)
    {
        var source = value.Trim()
            .Replace('ı', 'i')
            .Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(source.Length);
        var pendingSeparator = false;
        foreach (var character in source)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            var upper = char.ToUpperInvariant(character);
            if ((upper is >= 'A' and <= 'Z') || char.IsDigit(upper) || upper is '.' or '_')
            {
                if (pendingSeparator && result.Length > 0 && result[^1] is not '-' and not '.' and not '_')
                    result.Append('-');
                result.Append(upper);
                pendingSeparator = false;
            }
            else if (upper == '-')
            {
                pendingSeparator = result.Length > 0;
            }
            else
            {
                pendingSeparator = result.Length > 0;
            }
        }
        return result.ToString().Trim('-', '.', '_');
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
                throw AppException.BadRequest("XLSX dosyası en fazla 64 MB olabilir.");
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
        IReadOnlyList<long> WarehouseIds,
        IReadOnlyList<FlatRow> Rows,
        IReadOnlyList<FlatRow> NewLocations,
        int ExistingLocationCount,
        int InferredLocationMetadataCount,
        int NormalizedLocationCodeCount,
        bool UsedCustomerBalanceFormat,
        int SupportZoneCount,
        byte[] LocationWorkbook,
        byte[] BalanceWorkbook,
        decimal TotalQuantity,
        int BatchCount);

    private sealed record WarehouseLookupRow(long Id, int WarehouseCode, string BranchCode);
    private sealed record LocationLookupRow(long WarehouseId, string Code);
    private sealed record StockReferenceRow(long Id, string BranchCode, string StockCode);
    private sealed record YapReferenceRow(string BranchCode, string Code, long? StockId);

    private enum WorkbookFormat
    {
        Standard,
        CustomerBalance
    }
}
