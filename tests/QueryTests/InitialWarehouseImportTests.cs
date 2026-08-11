using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class InitialWarehouseImportTests
{
    [Fact]
    public async Task Location_template_contains_current_branch_warehouses_and_hierarchy_guidance()
    {
        await using var db = CreateDb();
        db.AddRange(
            new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" },
            new Warehouse { BranchCode = "1", WarehouseCode = 2, WarehouseName = "Diğer" });
        await db.SaveChangesAsync();
        var service = new LocationImportService(Uow(db), new NoopLocationService());

        var bytes = await service.CreateTemplateAsync("0");

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("WarehouseCode", workbook.Worksheet("Raf Tanımları").Cell(1, 1).GetString());
        Assert.Equal("Merkez", workbook.Worksheet("Depolar").Cell(2, 2).GetString());
        Assert.DoesNotContain(workbook.Worksheet("Depolar").CellsUsed().Select(x => x.GetString()), x => x == "Diğer");
        Assert.Equal(XLAllowedValues.List, workbook.Worksheet("Raf Tanımları").Cell(2, 4).GetDataValidation().AllowedValues);
    }

    [Fact]
    public async Task Opening_balance_template_contains_active_locations_and_stock_references()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        db.Add(warehouse);
        await db.SaveChangesAsync();
        db.AddRange(
            new WarehouseLocation
            {
                BranchCode = "0", WarehouseId = warehouse.Id, Code = "A01-R01-G01", Name = "Göz 1",
                LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
            },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();
        var service = new OpeningBalanceImportService(Uow(db), new RecordingStockMovementService());

        var bytes = await service.CreateTemplateAsync("0");

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("A01-R01-G01", workbook.Worksheet("Aktif Raflar").Cell(2, 3).GetString());
        Assert.Equal("STK-01", workbook.Worksheet("Stoklar").Cell(2, 1).GetString());
        Assert.Contains("hareket defteri", workbook.Worksheet("Açıklamalar").Cell("A3").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(XLAllowedValues.List, workbook.Worksheet("İlk Raf Bakiyeleri").Cell(2, 9).GetDataValidation().AllowedValues);
    }

    [Fact]
    public async Task Location_import_creates_parent_before_child_even_when_excel_order_is_reversed()
    {
        await using var db = CreateDb();
        db.Add(new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" });
        await db.SaveChangesAsync();
        var recorder = new RecordingLocationService();
        var service = new LocationImportService(Uow(db), recorder);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Raf Tanımları");
        var headers = new[]
        {
            "WarehouseCode", "LocationCode", "LocationName", "LocationType", "ParentLocationCode",
            "BarcodeEntryMode", "Barcode", "ZoneCode", "AisleNo", "RackNo", "LevelNo", "BinNo",
            "CapacityQuantity", "CapacityWeight", "CapacityVolume", "CapacityUnit",
            "AllowMixedStock", "AllowMixedLot", "AllowMixedStatus", "AllowCycleCount",
            "IsPickable", "IsPutaway", "IsQuarantine", "IsActive", "Description"
        };
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        WriteLocationRow(sheet, 2, "A01-R01", "Rack", "A01");
        WriteLocationRow(sheet, 3, "A01", "Zone", null);
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var result = await service.ImportAsync(stream, "0");

        Assert.Equal(2, result.CreatedRows);
        Assert.Collection(recorder.Requests,
            parent => { Assert.Equal("A01", parent.Code); Assert.Null(parent.ParentLocationId); },
            child => { Assert.Equal("A01-R01", child.Code); Assert.Equal(1001, child.ParentLocationId); });
    }

    [Fact]
    public async Task Opening_balance_import_posts_auditable_adjustment_increase_instead_of_writing_projection()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        db.Add(warehouse);
        await db.SaveChangesAsync();
        var location = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "A01-R01-G01", Name = "Göz 1",
            LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        };
        var stock = new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" };
        db.AddRange(location, stock);
        await db.SaveChangesAsync();
        var recorder = new RecordingStockMovementService();
        var service = new OpeningBalanceImportService(Uow(db), recorder);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("İlk Raf Bakiyeleri");
        var headers = new[]
        {
            "WarehouseCode", "LocationCode", "StockCode", "YapCode", "Quantity", "UnitCode",
            "LotNo", "SerialNo", "StockStatus", "OccurredAt", "Description"
        };
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        object[] values = [1, "A01-R01-G01", "STK-01", "", 12.5m, "ADET", "", "", "Available", "", "Devir"];
        for (var column = 0; column < values.Length; column++) sheet.Cell(2, column + 1).Value = XLCellValue.FromObject(values[column]);
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var result = await service.ImportAsync(stream, "0", "opening-test-0001");

        Assert.False(result.IsReplay);
        Assert.NotNull(recorder.LastRequest);
        Assert.Equal(StockMovementTypes.AdjustmentIncrease, recorder.LastRequest!.OperationType);
        Assert.Equal("OpeningBalanceImport", recorder.LastRequest.ReferenceType);
        var line = Assert.Single(recorder.LastRequest.Lines);
        Assert.Equal(location.Id, line.TargetLocationId);
        Assert.Equal(12.5m, line.Quantity);
        Assert.Equal("Available", line.StockStatus);
    }

    [Fact]
    public async Task Opening_balance_retry_skips_validation_for_an_already_committed_batch()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        db.Add(warehouse);
        await db.SaveChangesAsync();
        db.AddRange(
            new WarehouseLocation
            {
                BranchCode = "0", WarehouseId = warehouse.Id, Code = "A01", Name = "A01",
                LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
            },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();

        await using var stream = OpeningBalanceWorkbook(501);
        var initialRecorder = new RecordingStockMovementService();
        var initialService = new OpeningBalanceImportService(Uow(db), initialRecorder);
        await initialService.ValidateWarehouseOpeningAsync(stream, "0");
        Assert.Equal(2, initialRecorder.ValidatedRequests.Count);
        var firstBatch = initialRecorder.ValidatedRequests[0];
        db.Add(new StockMovementOperation
        {
            BranchCode = "0",
            IdempotencyKey = firstBatch.IdempotencyKey,
            RequestHash = "existing",
            OperationType = StockMovementTypes.AdjustmentIncrease,
            ReferenceType = "OpeningBalanceImport",
            ReferenceNo = firstBatch.ReferenceNo,
            OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var recorder = new RecordingStockMovementService();
        var service = new OpeningBalanceImportService(Uow(db), recorder);
        stream.Position = 0;

        var validation = await service.ValidateWarehouseOpeningAsync(stream, "0");

        Assert.Equal(2, validation.BatchCount);
        Assert.Single(recorder.ValidatedRequests);
        Assert.Single(recorder.ValidatedRequests[0].Lines);
    }

    [Fact]
    public async Task Combined_opening_preview_deduplicates_repeated_location_and_preserves_each_serial_row()
    {
        await using var db = CreateDb();
        db.Add(new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" });
        await db.SaveChangesAsync();
        var locations = new RecordingLocationImportService();
        var balances = new RecordingOpeningBalanceImportService();
        var service = new WarehouseOpeningImportService(Uow(db), locations, balances);
        await using var stream = CombinedOpeningWorkbook(
            [1, "A01-R01-G01", "Göz 1", "", "STK-01", 1m, "SR-001"],
            [1, "A01-R01-G01", "", "Cell", "STK-01", 1m, "SR-002"],
            [1, "A01-R01-G01", "", "", "STK-02", 5m, ""]);

        var preview = await service.PreviewAsync(stream, "0");

        Assert.Equal(1, preview.NewLocationCount);
        Assert.Equal(3, preview.BalanceRowCount);
        Assert.Equal(2, preview.DistinctStockCount);
        Assert.Equal(2, preview.SerialCount);
        Assert.Equal(7m, preview.TotalQuantity);
        Assert.Equal(0, locations.ImportedRows);
        Assert.Equal(3, balances.ImportedRows);
    }

    [Fact]
    public async Task Combined_opening_preview_rejects_conflicting_metadata_for_same_location()
    {
        await using var db = CreateDb();
        db.Add(new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" });
        await db.SaveChangesAsync();
        var service = new WarehouseOpeningImportService(
            Uow(db), new RecordingLocationImportService(), new RecordingOpeningBalanceImportService());
        await using var stream = CombinedOpeningWorkbook(
            [1, "A01-R01-G01", "Göz 1", "Cell", "STK-01", 1m, "SR-001"],
            [1, "A01-R01-G01", "Başka Göz", "Cell", "STK-02", 1m, "SR-002"]);

        var error = await Assert.ThrowsAsync<verii_wms_api_v2.Shared.Application.Exceptions.AppException>(
            () => service.PreviewAsync(stream, "0"));

        Assert.Contains("LocationName", error.Message);
        Assert.Contains("çelişemez", error.Message);
    }

    [Fact]
    public async Task Combined_opening_preview_accepts_fifty_thousand_serial_rows_in_bounded_batches()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        db.Add(warehouse);
        await db.SaveChangesAsync();
        db.AddRange(
            new WarehouseLocation
            {
                BranchCode = "0", WarehouseId = warehouse.Id, Code = "A01-R01-G01", Name = "Göz 1",
                LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
            },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();
        var uow = Uow(db);
        var service = new WarehouseOpeningImportService(
            uow,
            new RecordingLocationImportService(),
            new OpeningBalanceImportService(uow, new RecordingStockMovementService()));
        await using var stream = FiftyThousandRowCombinedOpeningWorkbook();

        var preview = await service.PreviewAsync(stream, "0");

        Assert.Equal(50_000, preview.BalanceRowCount);
        Assert.Equal(50_000, preview.SerialCount);
        Assert.Equal(50_000m, preview.TotalQuantity);
        Assert.Equal(100, preview.BatchCount);
    }

    private static MemoryStream CombinedOpeningWorkbook(params object[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Depo Açılışları");
        var headers = new[]
        {
            "WarehouseCode", "LocationCode", "LocationName", "LocationType", "ParentLocationCode",
            "Barcode", "ZoneCode", "AisleNo", "RackNo", "LevelNo", "BinNo",
            "IsPickable", "IsPutaway", "IsQuarantine",
            "StockCode", "YapCode", "Quantity", "UnitCode", "LotNo", "SerialNo",
            "StockStatus", "OccurredAt", "Description"
        };
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        for (var index = 0; index < rows.Length; index++)
        {
            var source = rows[index];
            object?[] values =
            [
                source[0], source[1], source[2], source[3], null,
                null, null, null, null, null, null,
                true, true, false,
                source[4], null, source[5], "ADET", null, source[6],
                "Available", null, "İlk açılış"
            ];
            for (var column = 0; column < values.Length; column++)
                if (values[column] is not null)
                    sheet.Cell(index + 2, column + 1).Value = XLCellValue.FromObject(values[column]);
        }
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream FiftyThousandRowCombinedOpeningWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Depo Açılışları");
        var headers = new[]
        {
            "WarehouseCode", "LocationCode", "LocationName", "LocationType", "ParentLocationCode",
            "Barcode", "ZoneCode", "AisleNo", "RackNo", "LevelNo", "BinNo",
            "IsPickable", "IsPutaway", "IsQuarantine",
            "StockCode", "YapCode", "Quantity", "UnitCode", "LotNo", "SerialNo",
            "StockStatus", "OccurredAt", "Description"
        };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        for (var index = 0; index < 50_000; index++)
        {
            var row = index + 2;
            sheet.Cell(row, 1).Value = 1;
            sheet.Cell(row, 2).Value = "A01-R01-G01";
            sheet.Cell(row, 15).Value = "STK-01";
            sheet.Cell(row, 17).Value = 1m;
            sheet.Cell(row, 18).Value = "ADET";
            sheet.Cell(row, 20).Value = $"SR-{index + 1:D6}";
            sheet.Cell(row, 21).Value = "Available";
        }
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream OpeningBalanceWorkbook(int rowCount)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("İlk Raf Bakiyeleri");
        var headers = new[]
        {
            "WarehouseCode", "LocationCode", "StockCode", "YapCode", "Quantity", "UnitCode",
            "LotNo", "SerialNo", "StockStatus", "OccurredAt", "Description"
        };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        for (var index = 0; index < rowCount; index++)
        {
            var row = index + 2;
            sheet.Cell(row, 1).Value = 1;
            sheet.Cell(row, 2).Value = "A01";
            sheet.Cell(row, 3).Value = "STK-01";
            sheet.Cell(row, 5).Value = 1m;
            sheet.Cell(row, 6).Value = "ADET";
            sheet.Cell(row, 8).Value = $"SR-{index + 1:D6}";
            sheet.Cell(row, 9).Value = "Available";
        }
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static void WriteLocationRow(IXLWorksheet sheet, int row, string code, string type, string? parent)
    {
        object?[] values =
        [
            1, code, code, type, parent, "Auto", null, null, null, null, null, null, null, null, null, null,
            false, false, false, true, true, true, false, true, null
        ];
        for (var column = 0; column < values.Length; column++)
            if (values[column] is not null) sheet.Cell(row, column + 1).Value = XLCellValue.FromObject(values[column]);
    }

    private static UnitOfWork Uow(WmsDbContext db) => new(db, new HttpContextAccessor());
    private static WmsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new WmsDbContext(options);
    }

    private sealed class NoopLocationService : ILocationService
    {
        public Task<long> CreateAsync(LocationUpsertRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocationGridRow> GetByIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LocationLookupRow>> GetLookupAsync(long warehouseId, bool includeInactive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponse<LocationGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PutawayLocationSuggestion>> GetPutawaySuggestionsAsync(long warehouseId, long? stockId, string? stockCode, long? yapCodeId, decimal quantity, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocationStats> GetStatsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(long id, LocationUpsertRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingLocationService : ILocationService
    {
        public List<LocationUpsertRequest> Requests { get; } = [];
        public Task<long> CreateAsync(LocationUpsertRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(1000L + Requests.Count);
        }
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocationGridRow> GetByIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LocationLookupRow>> GetLookupAsync(long warehouseId, bool includeInactive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponse<LocationGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PutawayLocationSuggestion>> GetPutawaySuggestionsAsync(long warehouseId, long? stockId, string? stockCode, long? yapCodeId, decimal quantity, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LocationStats> GetStatsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(long id, LocationUpsertRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingStockMovementService : IStockMovementService
    {
        public PostStockMovementRequest? LastRequest { get; private set; }
        public List<PostStockMovementRequest> ValidatedRequests { get; } = [];

        public Task ValidateAsync(PostStockMovementRequest request, CancellationToken cancellationToken = default)
        {
            ValidatedRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task<StockMovementPostResult> PostAsync(PostStockMovementRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new StockMovementPostResult(1, Guid.NewGuid(), false, request.Lines.Count));
        }
        public Task<StockMovementDetail> GetByIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponse<StockMovementGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockMovementPostResult> ReverseAsync(long operationId, ReverseStockMovementRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingLocationImportService : ILocationImportService
    {
        public int ImportedRows { get; private set; }
        public string? ImportedName { get; private set; }
        public string? ImportedType { get; private set; }

        public Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<byte>());

        public Task<LocationImportResult> ImportAsync(
            Stream workbookStream,
            string branchCode,
            CancellationToken cancellationToken = default)
        {
            using var workbook = new XLWorkbook(workbookStream);
            var rows = workbook.Worksheet("Raf Tanımları").RowsUsed().Where(x => x.RowNumber() > 1).ToList();
            ImportedRows = rows.Count;
            ImportedName = rows.FirstOrDefault()?.Cell(3).GetString();
            ImportedType = rows.FirstOrDefault()?.Cell(4).GetString();
            return Task.FromResult(new LocationImportResult(
                rows.Count, rows.Count, 0,
                rows.Select(x => new LocationImportRowResult(
                    x.RowNumber(), "Created", x.Cell(1).GetString(), x.Cell(2).GetString(), "OK")).ToList()));
        }
    }

    private sealed class RecordingOpeningBalanceImportService : IOpeningBalanceImportService
    {
        public int ImportedRows { get; private set; }

        public Task<byte[]> CreateTemplateAsync(string branchCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<byte>());

        public Task<OpeningBalanceImportResult> ImportAsync(
            Stream workbookStream,
            string branchCode,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
            => RecordAsync(workbookStream);

        public Task<OpeningBalanceImportResult> ImportWarehouseOpeningAsync(
            Stream workbookStream,
            string branchCode,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
            => RecordAsync(workbookStream);

        public async Task<OpeningBalanceImportValidation> ValidateWarehouseOpeningAsync(
            Stream workbookStream,
            string branchCode,
            CancellationToken cancellationToken = default)
        {
            var result = await RecordAsync(workbookStream);
            return new(result.TotalRows, result.TotalQuantity,
                (int)Math.Ceiling(result.TotalRows / (decimal)OpeningBalanceImportService.MovementBatchSize));
        }

        private Task<OpeningBalanceImportResult> RecordAsync(Stream workbookStream)
        {
            using var workbook = new XLWorkbook(workbookStream);
            var rows = workbook.Worksheet("İlk Raf Bakiyeleri").RowsUsed().Where(x => x.RowNumber() > 1).ToList();
            ImportedRows = rows.Count;
            var total = rows.Sum(x => x.Cell(5).GetValue<decimal>());
            return Task.FromResult(new OpeningBalanceImportResult(
                1, Guid.NewGuid(), false, rows.Count, total,
                rows.Select(x => new OpeningBalanceImportRowResult(
                    x.RowNumber(), "Posted", x.Cell(1).GetString(), x.Cell(2).GetString(),
                    x.Cell(3).GetString(), "OK")).ToList()));
        }
    }
}
