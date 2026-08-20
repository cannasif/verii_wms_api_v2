using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseBarcodeResolutionServiceTests
{
    [Fact]
    public async Task Split_source_label_is_rejected_and_generated_child_remains_resolvable()
    {
        await using var db = CreateDb();
        var stock = new Stock
        {
            BranchCode = "0",
            ErpStockCode = "STK-001",
            StockName = "Test stok",
            BaseUnitCode = "AD"
        };
        db.Add(stock);
        await db.SaveChangesAsync();
        db.AddRange(
            new GoodsReceiptLabel
            {
                BranchCode = "0",
                StockId = stock.Id,
                StockCodeSnapshot = stock.ErpStockCode,
                LabelQuantity = 10,
                UnitCode = "AD",
                SerialNo = "PALLET-1",
                BarcodeValue = "OLD-SPLIT-LABEL",
                Status = GoodsReceiptLabelStatus.Split
            },
            new GoodsReceiptLabel
            {
                BranchCode = "0",
                StockId = stock.Id,
                StockCodeSnapshot = stock.ErpStockCode,
                LabelQuantity = 4,
                UnitCode = "AD",
                SerialNo = "PALLET-1",
                BarcodeValue = "NEW-CHILD-LABEL",
                Status = GoodsReceiptLabelStatus.Generated
            });
        await db.SaveChangesAsync();

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var resolver = new WarehouseBarcodeResolutionService(
            uow,
            new FixedTrackingPolicyResolver(stock.Id, stock.ErpStockCode, SerialQuantityRule.OneSerialPerLine));

        await Assert.ThrowsAsync<AppException>(() => resolver.ResolveAsync(new(
            "OLD-SPLIT-LABEL", "0", WarehouseBarcodePurpose.Inbound, null, stock.Id)));

        var child = await resolver.ResolveAsync(new(
            "NEW-CHILD-LABEL", "0", WarehouseBarcodePurpose.Inbound, null, stock.Id));
        Assert.Equal("GoodsReceiptLabel", child.Source);
        Assert.Equal(4, child.Quantity);
        Assert.Equal("PALLET-1", child.SerialNo);
    }

    [Theory]
    [InlineData(SerialQuantityRule.OneSerialPerBaseUnit, 1)]
    [InlineData(SerialQuantityRule.OneSerialPerLine, 9)]
    public async Task Outbound_expected_stock_context_still_resolves_plain_serial_barcode(
        SerialQuantityRule serialQuantityRule,
        decimal availableQuantity)
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock
        {
            BranchCode = "0",
            ErpStockCode = "STK-001",
            StockName = "Test stok",
            BaseUnitCode = "AD"
        };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            BranchCode = "0",
            WarehouseId = warehouse.Id,
            Code = "A1",
            Name = "Raf 1",
            IsActive = true,
            IsPickable = true
        };
        db.Add(location);
        await db.SaveChangesAsync();
        db.Add(new LocationStockBalance
        {
            BranchCode = "0",
            DimensionKey = $"SERIAL-{availableQuantity}",
            WarehouseId = warehouse.Id,
            LocationId = location.Id,
            StockId = stock.Id,
            UnitCode = "AD",
            SerialNo = "UTG-1",
            StockStatus = "Available",
            Quantity = availableQuantity,
            AvailableQuantity = availableQuantity,
            LastTransactionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var resolver = new WarehouseBarcodeResolutionService(
            uow,
            new FixedTrackingPolicyResolver(stock.Id, stock.ErpStockCode, serialQuantityRule));

        var result = await resolver.ResolveAsync(new(
            "UTG-1",
            "0",
            WarehouseBarcodePurpose.Outbound,
            warehouse.Id,
            stock.Id));

        Assert.True(result.CanExecute);
        Assert.Empty(result.MissingFields);
        Assert.Equal("SerialBalance", result.Source);
        Assert.Equal(stock.Id, result.StockId);
        Assert.Equal("UTG-1", result.SerialNo);
        Assert.Equal(availableQuantity, result.Quantity);
        var balance = Assert.Single(result.BalanceCandidates);
        Assert.Equal(location.Id, balance.LocationId);
        Assert.Equal(availableQuantity, balance.AvailableQuantity);
    }

    [Fact]
    public async Task Outbound_resolves_reserved_serial_when_available_quantity_is_zero()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock
        {
            BranchCode = "0",
            ErpStockCode = "STK-001",
            StockName = "Test stok",
            BaseUnitCode = "AD"
        };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            BranchCode = "0",
            WarehouseId = warehouse.Id,
            Code = "A1",
            Name = "Raf 1",
            IsActive = true,
            IsPickable = true
        };
        db.Add(location);
        await db.SaveChangesAsync();
        db.Add(new LocationStockBalance
        {
            BranchCode = "0",
            DimensionKey = "SERIAL-RESERVED",
            WarehouseId = warehouse.Id,
            LocationId = location.Id,
            StockId = stock.Id,
            UnitCode = "AD",
            SerialNo = "UTG-1",
            StockStatus = "Available",
            Quantity = 1,
            ReservedQuantity = 1,
            AvailableQuantity = 0,
            LastTransactionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var resolver = new WarehouseBarcodeResolutionService(
            uow,
            new FixedTrackingPolicyResolver(stock.Id, stock.ErpStockCode, SerialQuantityRule.OneSerialPerLine));

        var result = await resolver.ResolveAsync(new(
            "UTG-1", "0", WarehouseBarcodePurpose.Outbound, warehouse.Id, stock.Id));

        Assert.Equal("SerialBalance", result.Source);
        Assert.Equal("UTG-1", result.SerialNo);
        Assert.Equal(1, result.Quantity);
    }

    [Fact]
    public async Task Outbound_expected_location_resolves_serial_split_across_locations_from_selected_source()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock
        {
            BranchCode = "0",
            ErpStockCode = "STK-001",
            StockName = "Test stok",
            BaseUnitCode = "AD"
        };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();

        var source = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "A1", Name = "Kaynak",
            IsActive = true, IsPickable = true
        };
        var waiting = new WarehouseLocation
        {
            BranchCode = "0", WarehouseId = warehouse.Id, Code = "A2", Name = "Bekleme",
            IsActive = true, IsPutaway = true
        };
        db.AddRange(source, waiting);
        await db.SaveChangesAsync();
        db.AddRange(
            CreateSerialBalance(warehouse.Id, source.Id, stock.Id, "UTG-1", 41, "SRC"),
            CreateSerialBalance(warehouse.Id, waiting.Id, stock.Id, "UTG-1", 7, "WAIT"));
        await db.SaveChangesAsync();

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var resolver = new WarehouseBarcodeResolutionService(
            uow,
            new FixedTrackingPolicyResolver(stock.Id, stock.ErpStockCode, SerialQuantityRule.OneSerialPerLine));

        var result = await resolver.ResolveAsync(new(
            "UTG-1", "0", WarehouseBarcodePurpose.Outbound,
            warehouse.Id, stock.Id, source.Id));

        Assert.True(result.CanExecute);
        Assert.Equal("SerialBalance", result.Source);
        Assert.Equal("UTG-1", result.SerialNo);
        Assert.Equal(41, result.Quantity);
        var balance = Assert.Single(result.BalanceCandidates);
        Assert.Equal(source.Id, balance.LocationId);
        Assert.Equal(41, balance.AvailableQuantity);
    }

    [Fact]
    public async Task Outbound_expected_stock_rejects_unknown_text_instead_of_treating_it_as_stock_alias()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock
        {
            BranchCode = "0",
            ErpStockCode = "STK-001",
            StockName = "Test stok",
            BaseUnitCode = "AD"
        };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var resolver = new WarehouseBarcodeResolutionService(
            uow,
            new FixedTrackingPolicyResolver(stock.Id, stock.ErpStockCode, SerialQuantityRule.OneSerialPerLine));

        await Assert.ThrowsAsync<AppException>(() => resolver.ResolveAsync(new(
            "YANLIS-BARKOD", "0", WarehouseBarcodePurpose.Outbound,
            warehouse.Id, stock.Id)));
    }

    [Fact]
    public async Task Outbound_expected_stock_picks_best_balance_when_duplicate_serial_rows_exist()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock
        {
            BranchCode = "0",
            ErpStockCode = "01/013",
            StockName = "Test stok",
            BaseUnitCode = "AD"
        };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            BranchCode = "0",
            WarehouseId = warehouse.Id,
            Code = "A1",
            Name = "Raf 1",
            IsActive = true,
            IsPickable = true
        };
        db.Add(location);
        await db.SaveChangesAsync();
        db.AddRange(
            CreateSerialBalance(warehouse.Id, location.Id, stock.Id, "UTG-1", 1, "DIM-1"),
            CreateSerialBalance(warehouse.Id, location.Id, stock.Id, "UTG-1", 1, "DIM-2"));
        await db.SaveChangesAsync();

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var resolver = new WarehouseBarcodeResolutionService(
            uow,
            new FixedTrackingPolicyResolver(stock.Id, stock.ErpStockCode, SerialQuantityRule.OneSerialPerLine));

        var result = await resolver.ResolveAsync(new(
            "UTG-1",
            "0",
            WarehouseBarcodePurpose.Outbound,
            warehouse.Id,
            stock.Id,
            location.Id,
            null,
            "AD"));

        Assert.True(result.CanExecute);
        Assert.Equal("UTG-1", result.SerialNo);
        Assert.NotEmpty(result.BalanceCandidates);
    }

    [Fact]
    public async Task Outbound_plain_stock_code_starting_with_gs1_lot_ai_stays_stock_alias()
    {
        await using var db = CreateDb();
        var warehouse = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var stock = new Stock
        {
            BranchCode = "0",
            ErpStockCode = "100134-1",
            StockName = "Test stok",
            BaseUnitCode = "AD"
        };
        db.AddRange(warehouse, stock);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            BranchCode = "0",
            WarehouseId = warehouse.Id,
            Code = "A1",
            Name = "Raf 1",
            IsActive = true,
            IsPickable = true
        };
        db.Add(location);
        await db.SaveChangesAsync();
        db.Add(new LocationStockBalance
        {
            BranchCode = "0",
            DimensionKey = "NON-SERIAL",
            WarehouseId = warehouse.Id,
            LocationId = location.Id,
            StockId = stock.Id,
            UnitCode = "AD",
            StockStatus = "Available",
            Quantity = 310,
            AvailableQuantity = 303,
            ReservedQuantity = 7,
            LastTransactionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var resolver = new WarehouseBarcodeResolutionService(
            uow,
            new FixedTrackingPolicyResolver(
                stock.Id, stock.ErpStockCode, SerialQuantityRule.NotApplicable, requireSerial: false));

        var result = await resolver.ResolveAsync(new(
            "100134-1", "0", WarehouseBarcodePurpose.Outbound, warehouse.Id, stock.Id));

        Assert.True(result.CanExecute);
        Assert.Equal("StockAlias", result.Source);
        Assert.Equal(stock.Id, result.StockId);
        Assert.Null(result.LotNo);
        Assert.Null(result.SerialNo);
        var balance = Assert.Single(result.BalanceCandidates);
        Assert.Equal(303, balance.AvailableQuantity);
    }

    private static LocationStockBalance CreateSerialBalance(
        long warehouseId, long locationId, long stockId, string serialNo, decimal quantity, string dimensionKey) => new()
    {
        BranchCode = "0",
        DimensionKey = dimensionKey,
        WarehouseId = warehouseId,
        LocationId = locationId,
        StockId = stockId,
        UnitCode = "AD",
        SerialNo = serialNo,
        StockStatus = "Available",
        Quantity = quantity,
        AvailableQuantity = quantity,
        LastTransactionDate = DateTime.UtcNow
    };

    private static WmsDbContext CreateDb() => new(
        new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class FixedTrackingPolicyResolver(
        long stockId,
        string stockCode,
        SerialQuantityRule serialQuantityRule,
        bool requireSerial = true) : IStockTrackingPolicyResolver
    {
        public Task<EffectiveStockTrackingPolicy> ResolveAsync(
            string branchCode,
            long requestedStockId,
            CancellationToken ct = default)
        {
            Assert.Equal(stockId, requestedStockId);
            return Task.FromResult(new EffectiveStockTrackingPolicy(
                stockId,
                stockCode,
                null,
                requireSerial ? StockTrackingType.Serial : StockTrackingType.None,
                requireSerial,
                serialQuantityRule,
                false,
                false,
                false,
                false,
                null,
                true,
                "Stock",
                1,
                1,
                "TEST"));
        }
    }
}
