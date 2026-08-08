using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
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
        SerialQuantityRule serialQuantityRule) : IStockTrackingPolicyResolver
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
                StockTrackingType.Serial,
                true,
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
