using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Claims;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
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
    public async Task Location_import_creates_missing_location_by_warehouse_branch_not_login_branch()
    {
        await using var db = CreateDb();
        db.Add(new Warehouse { BranchCode = "7", WarehouseCode = 6000, WarehouseName = "6000 Deposu" });
        await db.SaveChangesAsync();
        var recorder = new RecordingLocationService();
        var service = new LocationImportService(Uow(db, "0"), recorder);
        await using var stream = LocationWorkbook(6000, "A01", "Zone");

        var result = await service.ImportAsync(stream, "0");

        Assert.Equal(1, result.CreatedRows);
        var request = Assert.Single(recorder.Requests);
        Assert.Equal("A01", request.Code);
        Assert.Equal(LocationTypes.Zone, request.LocationType);
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

    [Theory]
    [InlineData(5, 7, 2, true)]
    [InlineData(5, 3, 2, false)]
    public async Task Warehouse_opening_reconciliation_posts_only_the_required_difference(
        decimal currentQuantity,
        decimal excelQuantity,
        decimal expectedDifference,
        bool increase)
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var location = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = 0, Code = "A01", Name = "A01",
            LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        };
        var stock = new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();
        location.WarehouseId = warehouse.Id;
        db.Add(location);
        await db.SaveChangesAsync();
        db.Add(new LocationStockBalance
        {
            BranchCode = "0", DimensionKey = "test", WarehouseId = warehouse.Id,
            LocationId = location.Id, StockId = stock.Id, UnitCode = "ADET",
            StockStatus = "Available", Quantity = currentQuantity,
            AvailableQuantity = currentQuantity
        });
        await db.SaveChangesAsync();

        var recorder = new RecordingStockMovementService();
        var service = new OpeningBalanceImportService(Uow(db), recorder);
        var state = await service.AnalyzeWarehouseStateAsync([warehouse.Id]);
        await using var stream = OpeningBalanceWorkbookWithRows(
            [1, "A01", "STK-01", excelQuantity]);

        var result = await service.ImportWarehouseOpeningAsync(
            stream, "0", "reconcile-difference", true, state.SnapshotHash);

        Assert.False(result.IsReplay);
        var request = Assert.Single(recorder.PostedRequests);
        Assert.Equal(StockMovementTypes.BalanceReconciliation, request.OperationType);
        var line = Assert.Single(request.Lines);
        Assert.Equal(expectedDifference, line.Quantity);
        Assert.Equal(increase, line.TargetWarehouseId.HasValue);
        Assert.Equal(!increase, line.SourceWarehouseId.HasValue);
    }

    [Fact]
    public async Task Warehouse_opening_reconciliation_zeros_a_balance_omitted_from_excel()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var oldStock = new Stock { BranchCode = "0", ErpStockCode = "OLD", StockName = "Eski", BaseUnitCode = "ADET" };
        var newStock = new Stock { BranchCode = "0", ErpStockCode = "NEW", StockName = "Yeni", BaseUnitCode = "ADET" };
        db.AddRange(warehouse, oldStock, newStock);
        await db.SaveChangesAsync();
        var location = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "A01", Name = "A01",
            LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        };
        db.Add(location);
        await db.SaveChangesAsync();
        db.Add(new LocationStockBalance
        {
            BranchCode = "0", DimensionKey = "old", WarehouseId = warehouse.Id,
            LocationId = location.Id, StockId = oldStock.Id, UnitCode = "ADET",
            StockStatus = "Available", Quantity = 5, AvailableQuantity = 5
        });
        await db.SaveChangesAsync();

        var recorder = new RecordingStockMovementService();
        var service = new OpeningBalanceImportService(Uow(db), recorder);
        var state = await service.AnalyzeWarehouseStateAsync([warehouse.Id]);
        await using var stream = OpeningBalanceWorkbookWithRows([1, "A01", "NEW", 2m]);

        await service.ImportWarehouseOpeningAsync(
            stream, "0", "reconcile-omitted", true, state.SnapshotHash);

        var request = Assert.Single(recorder.PostedRequests);
        Assert.Equal(2, request.Lines.Count);
        var decrease = Assert.Single(request.Lines, x => x.StockId == oldStock.Id);
        Assert.Equal(5, decrease.Quantity);
        Assert.Equal(location.Id, decrease.SourceLocationId);
        var increase = Assert.Single(request.Lines, x => x.StockId == newStock.Id);
        Assert.Equal(2, increase.Quantity);
        Assert.Equal(location.Id, increase.TargetLocationId);
    }

    [Fact]
    public async Task Warehouse_opening_reconciliation_is_blocked_while_stock_is_reserved()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test", BaseUnitCode = "ADET" };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();
        var location = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "A01", Name = "A01",
            LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        };
        db.Add(location);
        await db.SaveChangesAsync();
        db.Add(new LocationStockBalance
        {
            BranchCode = "0", DimensionKey = "reserved", WarehouseId = warehouse.Id,
            LocationId = location.Id, StockId = stock.Id, UnitCode = "ADET",
            StockStatus = "Available", Quantity = 5, ReservedQuantity = 1, AvailableQuantity = 4
        });
        await db.SaveChangesAsync();
        var service = new OpeningBalanceImportService(Uow(db), new RecordingStockMovementService());
        var state = await service.AnalyzeWarehouseStateAsync([warehouse.Id]);
        await using var stream = OpeningBalanceWorkbookWithRows([1, "A01", "STK-01", 5m]);

        var error = await Assert.ThrowsAsync<verii_wms_api_v2.Shared.Application.Exceptions.AppException>(
            () => service.ImportWarehouseOpeningAsync(
                stream, "0", "reconcile-reserved", true, state.SnapshotHash));

        Assert.Contains("rezerve", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Warehouse_opening_reconciliation_creates_no_movement_when_excel_already_matches()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test", BaseUnitCode = "ADET" };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();
        var location = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "A01", Name = "A01",
            LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        };
        db.Add(location);
        await db.SaveChangesAsync();
        db.Add(new LocationStockBalance
        {
            BranchCode = "0", DimensionKey = "same", WarehouseId = warehouse.Id,
            LocationId = location.Id, StockId = stock.Id, UnitCode = "ADET",
            StockStatus = "Available", Quantity = 5, AvailableQuantity = 5
        });
        await db.SaveChangesAsync();
        var recorder = new RecordingStockMovementService();
        var service = new OpeningBalanceImportService(Uow(db), recorder);
        var state = await service.AnalyzeWarehouseStateAsync([warehouse.Id]);
        await using var stream = OpeningBalanceWorkbookWithRows([1, "A01", "STK-01", 5m]);

        var result = await service.ImportWarehouseOpeningAsync(
            stream, "0", "reconcile-no-change", true, state.SnapshotHash);

        Assert.False(result.IsReplay);
        Assert.Equal(0, result.BatchCount);
        Assert.Empty(recorder.PostedRequests);
    }

    [Fact]
    public async Task Warehouse_opening_reconciliation_rejects_a_stale_preview_snapshot()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test", BaseUnitCode = "ADET" };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();
        var location = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "A01", Name = "A01",
            LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        };
        db.Add(location);
        await db.SaveChangesAsync();
        var balance = new LocationStockBalance
        {
            BranchCode = "0", DimensionKey = "changed", WarehouseId = warehouse.Id,
            LocationId = location.Id, StockId = stock.Id, UnitCode = "ADET",
            StockStatus = "Available", Quantity = 5, AvailableQuantity = 5
        };
        db.Add(balance);
        await db.SaveChangesAsync();
        var service = new OpeningBalanceImportService(Uow(db), new RecordingStockMovementService());
        var previewState = await service.AnalyzeWarehouseStateAsync([warehouse.Id]);
        balance.Quantity = 6;
        balance.AvailableQuantity = 6;
        await db.SaveChangesAsync();
        await using var stream = OpeningBalanceWorkbookWithRows([1, "A01", "STK-01", 7m]);

        var error = await Assert.ThrowsAsync<verii_wms_api_v2.Shared.Application.Exceptions.AppException>(
            () => service.ImportWarehouseOpeningAsync(
                stream, "0", "reconcile-stale", true, previewState.SnapshotHash));

        Assert.Contains("ön doğrulamadan sonra değişti", error.Message);
    }

    [Fact]
    public async Task Combined_opening_preview_deduplicates_repeated_location_and_preserves_each_serial_row()
    {
        await using var db = CreateDb();
        db.AddRange(
            new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok 1", BaseUnitCode = "ADET" },
            new Stock { BranchCode = "0", ErpStockCode = "STK-02", StockName = "Test stok 2", BaseUnitCode = "ADET" });
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
        Assert.Equal(0, balances.ImportedRows);
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
    public async Task Combined_opening_import_infers_flat_location_metadata_and_normalizes_customer_code()
    {
        await using var db = CreateDb();
        db.AddRange(
            new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();
        var locations = new RecordingLocationImportService();
        var balances = new RecordingOpeningBalanceImportService();
        var service = new WarehouseOpeningImportService(Uow(db), locations, balances);
        await using var stream = CombinedOpeningWorkbook(
            [1, "MB - C01", "", "", "STK-01", 2m, ""]);

        var preview = await service.PreviewAsync(stream, "0");
        stream.Position = 0;
        await service.ImportAsync(stream, "0", preview.FileHash, "opening-customer-0001",
            false, preview.BalanceSnapshotHash);

        Assert.Equal(2, preview.NewLocationCount);
        Assert.Contains(preview.Warnings, x => x.Contains("tipi Rack", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(preview.Warnings, x => x.Contains("raf kodu", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(locations.ImportedDefinitions, x =>
            x.Code == "WMS-OPENING-ZONE" && x.Type == LocationTypes.Zone);
        Assert.Contains(locations.ImportedDefinitions, x =>
            x.Code == "MB-C01" && x.Name == "MB - C01" && x.Type == LocationTypes.Rack);
        Assert.Equal("MB-C01", balances.ImportedLocationCode);
    }

    [Fact]
    public async Task Combined_opening_import_preserves_supported_slash_and_hyphen_in_location_code()
    {
        await using var db = CreateDb();
        db.AddRange(
            new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();
        var locations = new RecordingLocationImportService();
        var balances = new RecordingOpeningBalanceImportService();
        var service = new WarehouseOpeningImportService(Uow(db), locations, balances);
        await using var stream = CombinedOpeningWorkbook(
            [1, "A/01-B", "A/01-B Raf", "Rack", "STK-01", 2m, ""]);

        var preview = await service.PreviewAsync(stream, "0");
        stream.Position = 0;
        await service.ImportAsync(stream, "0", preview.FileHash, "opening-slash-hyphen-0001",
            false, preview.BalanceSnapshotHash);

        Assert.Contains(locations.ImportedDefinitions, x => x.Code == "A/01-B");
        Assert.Equal("A/01-B", balances.ImportedLocationCode);
    }

    [Fact]
    public async Task Combined_opening_preview_accepts_seven_column_customer_balance_workbook()
    {
        await using var db = CreateDb();
        db.AddRange(
            new Warehouse { BranchCode = "0", WarehouseCode = 6000, WarehouseName = "Merkez" },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();
        var service = new WarehouseOpeningImportService(
            Uow(db, "0"), new RecordingLocationImportService(), new RecordingOpeningBalanceImportService());
        await using var stream = CustomerBalanceWorkbook();

        var preview = await service.PreviewAsync(stream, "0");

        Assert.Equal(1, preview.BalanceRowCount);
        Assert.Equal(67.86m, preview.TotalQuantity);
        Assert.Contains(preview.Warnings, x => x.Contains("7 kolonlu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Combined_opening_import_normalizes_turkish_location_type()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        db.Add(warehouse);
        await db.SaveChangesAsync();
        db.AddRange(new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "ZONE-1", Name = "Bölge 1",
            LocationType = LocationTypes.Zone, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        }, new Stock
        {
            BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET"
        });
        await db.SaveChangesAsync();
        var locations = new RecordingLocationImportService();
        var service = new WarehouseOpeningImportService(
            Uow(db), locations, new RecordingOpeningBalanceImportService());
        await using var stream = CombinedOpeningWorkbook(
            [1, "RAF-01", "Raf 1", "Raf", "STK-01", 1m, "", "ZONE-1"]);

        var preview = await service.PreviewAsync(stream, "0");
        stream.Position = 0;
        await service.ImportAsync(stream, "0", preview.FileHash, "opening-customer-0002",
            false, preview.BalanceSnapshotHash);

        Assert.Contains(locations.ImportedDefinitions, x => x.Code == "RAF-01" && x.Type == LocationTypes.Rack);
    }

    [Fact]
    public async Task Combined_opening_normalizes_single_character_location_names_consistently()
    {
        await using var db = CreateDb();
        db.AddRange(
            new Warehouse { BranchCode = "0", WarehouseCode = 4000, WarehouseName = "4000 Deposu" },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" },
            new Stock { BranchCode = "0", ErpStockCode = "STK-02", StockName = "Test stok 2", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();
        var locations = new RecordingLocationImportService();
        var service = new WarehouseOpeningImportService(
            Uow(db), locations, new RecordingOpeningBalanceImportService());
        await using var stream = CombinedOpeningWorkbook(
            [4000, "K", "K_Raf", "Rack", "STK-01", 1m, ""],
            [4000, "K", "K", "Rack", "STK-02", 1m, ""]);

        var preview = await service.PreviewAsync(stream, "0");
        stream.Position = 0;
        await service.ImportAsync(stream, "0", preview.FileHash, "single-location-name",
            false, preview.BalanceSnapshotHash);

        var location = Assert.Single(locations.ImportedDefinitions, x => x.Code == "K");
        Assert.Equal("K Raf", location.Name);
    }

    [Fact]
    public async Task Combined_opening_preview_rejects_quantity_above_movement_limit()
    {
        await using var db = CreateDb();
        db.AddRange(
            new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" },
            new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();
        var service = new WarehouseOpeningImportService(
            Uow(db), new RecordingLocationImportService(), new RecordingOpeningBalanceImportService());
        await using var stream = CombinedOpeningWorkbook(
            [1, "A01", "A01 Raf", "Rack", "STK-01", StockMovementLimits.MaxQuantity + 1m, ""]);

        var error = await Assert.ThrowsAsync<verii_wms_api_v2.Shared.Application.Exceptions.AppException>(
            () => service.PreviewAsync(stream, "0"));

        Assert.Contains("en fazla", error.Message);
        Assert.Contains("Satır 2", error.Message);
    }

    [Fact]
    public async Task Combined_opening_preview_requires_explicit_reconciliation_for_used_warehouse()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock { BranchCode = "0", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();
        var existingLocation = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "MEVCUT", Name = "Mevcut Raf",
            LocationType = LocationTypes.Rack, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        };
        db.Add(existingLocation);
        await db.SaveChangesAsync();
        var operation = new StockMovementOperation
        {
            BranchCode = "0", IdempotencyKey = "existing-movement", RequestHash = "existing",
            OperationType = StockMovementTypes.Receipt, Status = StockMovementStatuses.Posted,
            ReferenceType = "GoodsReceipt", OccurredAt = DateTime.UtcNow
        };
        db.Add(operation);
        await db.SaveChangesAsync();
        db.Add(new StockMovementEntry
        {
            BranchCode = "0", OperationId = operation.Id, LineNo = 1, StockId = stock.Id,
            WarehouseId = warehouse.Id, LocationId = existingLocation.Id, QuantityDelta = 1m,
            UnitCode = "ADET", StockStatus = "Available", OccurredAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var locations = new RecordingLocationImportService();
        var balances = new RecordingOpeningBalanceImportService
        {
            WarehouseState = new WarehouseOpeningBalanceState(
                new string('A', 64), 1, 1, 1m, 0, 0m)
        };
        var service = new WarehouseOpeningImportService(
            Uow(db), locations, balances);
        await using var stream = CombinedOpeningWorkbook(
            [1, "YENI", "Yeni Raf", "Rack", "STK-01", 1m, ""]);

        var preview = await service.PreviewAsync(stream, "0");

        Assert.True(preview.RequiresBalanceReplacement);
        Assert.Equal(1, preview.ExistingMovementCount);
        Assert.Equal(1m, preview.CurrentTotalQuantity);
        Assert.Equal(0, locations.ImportedRows);
    }

    [Fact]
    public async Task Combined_opening_uses_warehouse_branch_instead_of_logged_in_branch()
    {
        await using var db = CreateDb();
        db.AddRange(
            new Warehouse { BranchCode = "7", WarehouseCode = 6000, WarehouseName = "6000 Deposu" },
            new Stock { BranchCode = "7", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET" });
        await db.SaveChangesAsync();
        var service = new WarehouseOpeningImportService(
            Uow(db), new RecordingLocationImportService(), new RecordingOpeningBalanceImportService());
        await using var stream = CustomerBalanceWorkbook();

        var preview = await service.PreviewAsync(stream, "0");

        Assert.Equal(1, preview.WarehouseCount);
        Assert.Equal(1, preview.BalanceRowCount);
        Assert.Equal(2, preview.NewLocationCount);
    }

    [Fact]
    public async Task Opening_balance_import_posts_under_warehouse_branch_when_login_branch_is_different()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "7", WarehouseCode = 6000, WarehouseName = "6000 Deposu" };
        db.Add(warehouse);
        await db.SaveChangesAsync();
        var location = new WarehouseLocation
        {
            BranchCode = "7", WarehouseId = warehouse.Id, Code = "A01", Name = "A01",
            LocationType = LocationTypes.Cell, BarcodeEntryMode = BarcodeEntryModes.Auto, IsActive = true
        };
        var stock = new Stock
        {
            BranchCode = "7", ErpStockCode = "STK-01", StockName = "Test stok", BaseUnitCode = "ADET"
        };
        db.AddRange(location, stock);
        await db.SaveChangesAsync();
        var recorder = new RecordingStockMovementService();
        var service = new OpeningBalanceImportService(Uow(db, "0"), recorder);
        await using var stream = OpeningBalanceWorkbook(1, 6000, "A01");

        var result = await service.ImportWarehouseOpeningAsync(stream, "0", "warehouse-branch-test");

        Assert.False(result.IsReplay);
        var line = Assert.Single(recorder.LastRequest!.Lines);
        Assert.Equal(warehouse.Id, line.TargetWarehouseId);
        Assert.Equal(location.Id, line.TargetLocationId);
        Assert.Equal(stock.Id, line.StockId);
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
                source[0], source[1], source[2], source[3], source.Length > 7 ? source[7] : null,
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

    private static MemoryStream LocationWorkbook(
        int warehouseCode,
        string locationCode,
        string locationType,
        string? parentCode = null)
    {
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
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        object?[] values =
        [
            warehouseCode, locationCode, locationCode, locationType, parentCode, "Auto", null, null,
            null, null, null, null, null, null, null, null, false, false, false, true,
            true, true, false, true, "Test rafı"
        ];
        for (var column = 0; column < values.Length; column++)
            if (values[column] is not null)
                sheet.Cell(2, column + 1).Value = XLCellValue.FromObject(values[column]);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CustomerBalanceWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sayfa1");
        string[] headers = ["TARIH", "STOKKODU", "STOK_ADI", "SERI_NO", "DEPOKOD", "HUCREKODU", "BAKIYE"];
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        object?[] values = [new DateTime(2026, 8, 12), "STK-01", "Test stok", null, 6000, "BPROFIL", 67.86m];
        for (var column = 0; column < values.Length; column++)
            if (values[column] is not null) sheet.Cell(2, column + 1).Value = XLCellValue.FromObject(values[column]);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream OpeningBalanceWorkbook(
        int rowCount,
        int warehouseCode = 1,
        string locationCode = "A01")
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
            sheet.Cell(row, 1).Value = warehouseCode;
            sheet.Cell(row, 2).Value = locationCode;
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

    private static MemoryStream OpeningBalanceWorkbookWithRows(params object[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("İlk Raf Bakiyeleri");
        string[] headers =
        [
            "WarehouseCode", "LocationCode", "StockCode", "YapCode", "Quantity", "UnitCode",
            "LotNo", "SerialNo", "StockStatus", "OccurredAt", "Description"
        ];
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        for (var index = 0; index < rows.Length; index++)
        {
            var source = rows[index];
            object?[] values =
            [
                source[0], source[1], source[2], null, source[3], "ADET",
                null, null, "Available", null, "Kesin bakiye eşitlemesi"
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

    private static UnitOfWork Uow(WmsDbContext db, string? authenticatedBranch = null)
    {
        var accessor = new HttpContextAccessor();
        if (!string.IsNullOrWhiteSpace(authenticatedBranch))
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(JwtTokenIssuer.BranchCodeClaim, authenticatedBranch)],
                    "Test"))
            };
        }
        return new UnitOfWork(db, accessor);
    }
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
        public List<PostStockMovementRequest> PostedRequests { get; } = [];

        public Task ValidateAsync(PostStockMovementRequest request, CancellationToken cancellationToken = default)
        {
            ValidatedRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task<StockMovementPostResult> PostAsync(PostStockMovementRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            PostedRequests.Add(request);
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
        public IReadOnlyList<(string Code, string Name, string Type)> ImportedDefinitions { get; private set; } = [];

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
            ImportedDefinitions = rows.Select(x =>
                (x.Cell(2).GetString(), x.Cell(3).GetString(), x.Cell(4).GetString())).ToList();
            return Task.FromResult(new LocationImportResult(
                rows.Count, rows.Count, 0,
                rows.Select(x => new LocationImportRowResult(
                    x.RowNumber(), "Created", x.Cell(1).GetString(), x.Cell(2).GetString(), "OK")).ToList()));
        }
    }

    private sealed class RecordingOpeningBalanceImportService : IOpeningBalanceImportService
    {
        public int ImportedRows { get; private set; }
        public string? ImportedLocationCode { get; private set; }
        public WarehouseOpeningBalanceState WarehouseState { get; set; } = new(
            new string('0', 64), 0, 0, 0, 0, 0);

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
            bool replaceExistingBalances = false,
            string? expectedBalanceSnapshotHash = null,
            CancellationToken cancellationToken = default)
            => RecordAsync(workbookStream);

        public async Task<OpeningBalanceImportValidation> ValidateWarehouseOpeningAsync(
            Stream workbookStream,
            string branchCode,
            bool replaceExistingBalances = false,
            string? expectedBalanceSnapshotHash = null,
            CancellationToken cancellationToken = default)
        {
            var result = await RecordAsync(workbookStream);
            return new(result.TotalRows, result.TotalQuantity,
                (int)Math.Ceiling(result.TotalRows / (decimal)OpeningBalanceImportService.MovementBatchSize));
        }

        public Task<WarehouseOpeningBalanceState> AnalyzeWarehouseStateAsync(
            IReadOnlyCollection<long> warehouseIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WarehouseState);

        private Task<OpeningBalanceImportResult> RecordAsync(Stream workbookStream)
        {
            using var workbook = new XLWorkbook(workbookStream);
            var rows = workbook.Worksheet("İlk Raf Bakiyeleri").RowsUsed().Where(x => x.RowNumber() > 1).ToList();
            ImportedRows = rows.Count;
            ImportedLocationCode = rows.FirstOrDefault()?.Cell(2).GetString();
            var total = rows.Sum(x => x.Cell(5).GetValue<decimal>());
            return Task.FromResult(new OpeningBalanceImportResult(
                1, Guid.NewGuid(), false, rows.Count, total,
                rows.Select(x => new OpeningBalanceImportRowResult(
                    x.RowNumber(), "Posted", x.Cell(1).GetString(), x.Cell(2).GetString(),
                    x.Cell(3).GetString(), "OK")).ToList()));
        }
    }
}
