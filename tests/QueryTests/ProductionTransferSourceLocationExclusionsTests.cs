using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferSourceLocationExclusionsTests
{
    [Fact]
    public async Task FromHeaderAsync_keeps_pickable_goods_receipt_location_as_source()
    {
        await using var db = CreateDb();
        var source = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Kaynak" };
        var target = new Warehouse { BranchCode = "0", WarehouseCode = 2, WarehouseName = "Hedef" };
        db.AddRange(source, target);
        await db.SaveChangesAsync();

        var goodsReceipt = AddLocation(db, source.Id, "MK", pickable: true, putaway: true);
        var pickStaging = AddLocation(db, source.Id, "TOP", pickable: false, putaway: true);
        var pickShelf = AddLocation(db, source.Id, "A1", pickable: true, putaway: true);
        var targetDefault = AddLocation(db, target.Id, "H1", pickable: false, putaway: true);
        await db.SaveChangesAsync();

        source.DefaultProductionTransferLocationId = goodsReceipt.Id;
        source.ProductionPickingStagingLocationId = pickStaging.Id;
        target.DefaultProductionTransferLocationId = targetDefault.Id;
        await db.SaveChangesAsync();

        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = source.Id,
            TargetWarehouseId = target.Id,
            SourceStagingLocationId = pickStaging.Id,
            TargetPutawayLocationId = targetDefault.Id,
        };

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var excluded = await ProductionTransferSourceLocationExclusions.FromHeaderAsync(
            uow, header, [], CancellationToken.None);

        Assert.DoesNotContain(goodsReceipt.Id, excluded);
        Assert.DoesNotContain(pickShelf.Id, excluded);
        Assert.Contains(pickStaging.Id, excluded);
        Assert.Contains(targetDefault.Id, excluded);
    }

    [Fact]
    public async Task FromHeaderAsync_still_excludes_picking_staging_even_when_pickable()
    {
        await using var db = CreateDb();
        var source = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Kaynak" };
        var target = new Warehouse { BranchCode = "0", WarehouseCode = 2, WarehouseName = "Hedef" };
        db.AddRange(source, target);
        await db.SaveChangesAsync();

        var pickStaging = AddLocation(db, source.Id, "TOP", pickable: true, putaway: true);
        await db.SaveChangesAsync();
        source.ProductionPickingStagingLocationId = pickStaging.Id;
        await db.SaveChangesAsync();

        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = source.Id,
            TargetWarehouseId = target.Id,
            SourceStagingLocationId = pickStaging.Id,
        };

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var excluded = await ProductionTransferSourceLocationExclusions.FromHeaderAsync(
            uow, header, [], CancellationToken.None);

        Assert.Contains(pickStaging.Id, excluded);
    }

    [Fact]
    public async Task FromHeaderAsync_excludes_non_pickable_source_default_location()
    {
        await using var db = CreateDb();
        var source = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Kaynak" };
        var target = new Warehouse { BranchCode = "0", WarehouseCode = 2, WarehouseName = "Hedef" };
        db.AddRange(source, target);
        await db.SaveChangesAsync();

        var virtualDefault = AddLocation(db, source.Id, "VIRT", pickable: false, putaway: true);
        await db.SaveChangesAsync();
        source.DefaultProductionTransferLocationId = virtualDefault.Id;
        await db.SaveChangesAsync();

        var header = new WarehouseTransferHeader
        {
            SourceWarehouseId = source.Id,
            TargetWarehouseId = target.Id,
        };

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var excluded = await ProductionTransferSourceLocationExclusions.FromHeaderAsync(
            uow, header, [], CancellationToken.None);

        Assert.Contains(virtualDefault.Id, excluded);
    }

    private static WarehouseLocation AddLocation(
        WmsDbContext db,
        long warehouseId,
        string code,
        bool pickable,
        bool putaway)
    {
        var location = new WarehouseLocation
        {
            BranchCode = "0",
            WarehouseId = warehouseId,
            Code = code,
            Name = code,
            IsActive = true,
            IsPickable = pickable,
            IsPutaway = putaway,
        };
        db.Add(location);
        return location;
    }

    private static WmsDbContext CreateDb() => new(
        new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
}
