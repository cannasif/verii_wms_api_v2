using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Dashboard.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Summary_is_branch_scoped_and_counts_only_current_users_active_tasks()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        db.WarehouseStockBalances.AddRange(
            WarehouseBalance("0", 1),
            WarehouseBalance("0", 2),
            WarehouseBalance("1", 3));

        var ownGoodsReceipt = GoodsReceipt("0", "GR-001", now.AddMinutes(-3));
        var pendingGoodsReceipt = GoodsReceipt(
            "0",
            "GR-002",
            now.AddMinutes(-2),
            approvalStatus: OperationApprovalStatus.Pending);
        var otherBranchGoodsReceipt = GoodsReceipt("1", "GR-OTHER", now);
        db.GoodsReceiptHeaders.AddRange(
            ownGoodsReceipt,
            pendingGoodsReceipt,
            otherBranchGoodsReceipt);

        var ownShipment = Shipment("0", "SH-001", now.AddMinutes(-1));
        db.ShipmentHeaders.AddRange(
            ownShipment,
            Shipment("1", "SH-OTHER", now));

        ownGoodsReceipt.Tasks.Add(GoodsReceiptTask("0", 42));
        pendingGoodsReceipt.Tasks.Add(GoodsReceiptTask("0", 7));
        otherBranchGoodsReceipt.Tasks.Add(GoodsReceiptTask("1", 42));
        ownShipment.Tasks.Add(ShipmentTask("0", 42));
        ownShipment.Tasks.Add(ShipmentTask("0", 42, ShipmentTaskStatus.Completed));

        db.WarehouseTransferHeaders.AddRange(
            Transfer("0", WarehouseTransferStatus.Released),
            Transfer("0", WarehouseTransferStatus.Completed),
            Transfer("0", WarehouseTransferStatus.Cancelled),
            Transfer(
                "0",
                WarehouseTransferStatus.Released,
                WarehouseTransferBusinessContext.ProductionMaterialSupply),
            Transfer("1", WarehouseTransferStatus.Released));

        await db.SaveChangesAsync();

        var summary = await new DashboardService(db).GetSummaryAsync(42, " 0 ");

        Assert.Equal(2, summary.StockItemCount);
        Assert.Equal(2, summary.GoodsReceiptOrderCount);
        Assert.Equal(1, summary.ShipmentOrderCount);
        Assert.Equal(1, summary.PendingGoodsReceiptApprovalCount);
        Assert.Equal(2, summary.MyAssignedTaskCount);
        Assert.Equal(1, summary.ActiveTransferOrderCount);
        Assert.DoesNotContain(summary.RecentActivities, x => x.Title.Contains("OTHER"));
    }

    [Fact]
    public async Task Summary_counts_only_assigned_warehouses_for_restricted_users()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        db.Users.Add(new User
        {
            Id = 42,
            Username = "operator",
            Email = "operator@test.local",
            PasswordHash = "x",
            Role = "User",
            IsActive = true,
        });
        db.UserWarehouseAssignments.Add(new UserWarehouseAssignment
        {
            UserId = 42,
            WarehouseId = 2,
            BranchCode = "0",
        });

        db.WarehouseStockBalances.AddRange(
            WarehouseBalance("0", 1, warehouseId: 1),
            WarehouseBalance("0", 2, warehouseId: 2));
        db.GoodsReceiptHeaders.AddRange(
            GoodsReceipt("0", "GR-WH1", now, warehouseId: 1),
            GoodsReceipt("0", "GR-WH2", now, warehouseId: 2));
        db.ShipmentHeaders.AddRange(
            Shipment("0", "SH-WH1", now, warehouseId: 1),
            Shipment("0", "SH-WH2", now, warehouseId: 2));

        await db.SaveChangesAsync();

        var summary = await new DashboardService(db).GetSummaryAsync(42, "0");

        Assert.Equal(1, summary.StockItemCount);
        Assert.Equal(1, summary.GoodsReceiptOrderCount);
        Assert.Equal(1, summary.ShipmentOrderCount);
        Assert.Equal("GR-WH2", summary.RecentActivities.Single(x => x.Kind == "goods-receipt").Title);
    }

    [Fact]
    public async Task Summary_returns_latest_eight_activities_with_stable_contract_values()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        for (var index = 0; index < 6; index++)
        {
            db.GoodsReceiptHeaders.Add(GoodsReceipt(
                "0",
                $"GR-{index}",
                now.AddMinutes(-index),
                index == 0 ? WarehouseOperationStatus.Completed : WarehouseOperationStatus.Released,
                index == 1 ? OperationApprovalStatus.Pending : OperationApprovalStatus.NotRequired));
            db.ShipmentHeaders.Add(Shipment(
                "0",
                $"SH-{index}",
                now.AddMinutes(-index).AddSeconds(-30),
                index == 0 ? ShipmentStatus.Shipped : ShipmentStatus.Released,
                index == 1 ? OperationApprovalStatus.Pending : OperationApprovalStatus.NotRequired));
        }

        await db.SaveChangesAsync();

        var summary = await new DashboardService(db).GetSummaryAsync(42, null);

        Assert.Equal(8, summary.RecentActivities.Count);
        Assert.Equal("GR-0", summary.RecentActivities[0].Title);
        Assert.Equal("goods-receipt", summary.RecentActivities[0].Kind);
        Assert.Equal("completed", summary.RecentActivities[0].Status);
        Assert.Equal("completed", summary.RecentActivities[1].Status);
        Assert.Equal("pending", summary.RecentActivities[2].Status);
        Assert.EndsWith("Z", summary.RecentActivities[0].Timestamp);
    }

    [Fact]
    public async Task Quick_search_finds_documents_and_stock_not_menus_and_stays_branch_scoped()
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        db.Stocks.AddRange(
            new Stock { ErpStockCode = "STK-100", StockName = "Civata M10", BaseUnitCode = "ADET" },
            new Stock { ErpStockCode = "01/001", StockName = "Intel Core i7", BaseUnitCode = "AD" },
            new Stock { ErpStockCode = "STK-200", StockName = "Somun M10", BaseUnitCode = "ADET" });

        db.GoodsReceiptHeaders.AddRange(
            GoodsReceipt("0", "MK-88421", now.AddMinutes(-2)),
            GoodsReceipt("1", "MK-88421-OTHER", now));
        db.ShipmentHeaders.Add(Shipment("0", "SV-110", now.AddMinutes(-1)));
        db.WarehouseTransferHeaders.Add(Transfer("0", WarehouseTransferStatus.Released, documentNo: "TR-77"));

        await db.SaveChangesAsync();

        var service = new DashboardService(db);
        var documentHits = await service.GetQuickSearchAsync(42, "0", "MK-884");
        var stockHits = await service.GetQuickSearchAsync(42, "0", "civata");
        var slashStockHits = await service.GetQuickSearchAsync(42, "0", "01/001");
        var turkishHits = await service.GetQuickSearchAsync(42, "0", "CİVATA");
        var emptyHits = await service.GetQuickSearchAsync(42, "0", "x");

        Assert.Contains(documentHits.Items, x => x.Kind == "goods-receipt" && x.Title == "MK-88421");
        Assert.DoesNotContain(documentHits.Items, x => x.Title.Contains("OTHER"));
        Assert.DoesNotContain(documentHits.Items, x => x.Href.Contains("/dashboard") || x.Kind == "menu");
        Assert.Contains(stockHits.Items, x => x.Kind == "stock" && x.Title == "STK-100");
        Assert.Contains(slashStockHits.Items, x => x.Kind == "stock" && x.Title == "01/001");
        Assert.Contains(turkishHits.Items, x => x.Kind == "stock" && x.Title == "STK-100");
        Assert.Empty(emptyHits.Items);
    }

    [Fact]
    public async Task Quick_search_finds_warehouse_location_serial_and_lot_without_dropping_stock_hits()
    {
        await using var db = CreateDbContext();
        db.Warehouses.AddRange(
            new verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse
            {
                Id = 8,
                WarehouseCode = 88,
                WarehouseName = "Magaza Depo",
            },
            new verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse
            {
                Id = 9,
                WarehouseCode = 3,
                WarehouseName = "SATIS DEPO",
            });
        db.Locations.Add(new verii_wms_api_v2.Modules.Location.Domain.WarehouseLocation
        {
            Id = 21,
            WarehouseId = 8,
            Code = "A-01",
            Name = "Koridor A Raf 01",
        });
        db.Stocks.Add(new Stock { Id = 5, ErpStockCode = "STK-500", StockName = "Civata M8", BaseUnitCode = "AD" });
        db.LocationStockBalances.Add(new LocationStockBalance
        {
            WarehouseId = 8,
            LocationId = 21,
            StockId = 5,
            LotNo = "LOT-9A",
            SerialNo = "SN-9991",
            Quantity = 1,
            AvailableQuantity = 1,
            LastTransactionDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = new DashboardService(db);
        var warehouseHits = await service.GetQuickSearchAsync(42, "0", "magaza");
        var salesWarehouseHits = await service.GetQuickSearchAsync(42, "0", "SATIS");
        var warehouseCodeHits = await service.GetQuickSearchAsync(42, "0", "3");
        var warehouseCodeStockOnly = await service.GetQuickSearchAsync(42, "0", "3", "stock");
        var locationHits = await service.GetQuickSearchAsync(42, "0", "A-01");
        var serialHits = await service.GetQuickSearchAsync(42, "0", "SN-9991");
        var lotHits = await service.GetQuickSearchAsync(42, "0", "LOT-9");
        var stockHits = await service.GetQuickSearchAsync(42, "0", "civata");

        Assert.Contains(warehouseHits.Items, x => x.Kind == "warehouse" && x.Title == "Magaza Depo");
        Assert.Contains(salesWarehouseHits.Items, x => x.Kind == "warehouse" && x.Title == "SATIS DEPO");
        Assert.Contains(warehouseCodeHits.Items, x => x.Kind == "warehouse" && x.Title == "SATIS DEPO");
        Assert.DoesNotContain(warehouseCodeHits.Items, x => x.Kind == "stock");
        Assert.Empty(warehouseCodeStockOnly.Items);
        Assert.Contains(locationHits.Items, x => x.Kind == "location" && x.Title == "A-01");
        Assert.Contains(serialHits.Items, x => x.Kind == "serial" && x.Title == "SN-9991");
        Assert.Contains(lotHits.Items, x => x.Kind == "lot" && x.Title == "LOT-9A");
        Assert.Contains(stockHits.Items, x => x.Kind == "stock" && x.Title == "STK-500");
    }

    [Theory]
    [InlineData(null, "0")]
    [InlineData("", "0")]
    [InlineData(" 01 ", "01")]
    public void Branch_code_is_normalized(string? value, string expected)
    {
        Assert.Equal(expected, DashboardService.NormalizeBranchCode(value));
    }

    private static WmsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new WmsDbContext(options);
    }

    private static WarehouseStockBalance WarehouseBalance(string branchCode, long stockId, long warehouseId = 1) => new()
    {
        BranchCode = branchCode,
        DimensionKey = $"{branchCode}:{warehouseId}:{stockId}",
        WarehouseId = warehouseId,
        StockId = stockId,
        LastTransactionDate = DateTime.UtcNow
    };

    private static GoodsReceiptHeader GoodsReceipt(
        string branchCode,
        string documentNo,
        DateTime timestamp,
        WarehouseOperationStatus status = WarehouseOperationStatus.Released,
        OperationApprovalStatus approvalStatus = OperationApprovalStatus.NotRequired,
        long warehouseId = 1) => new()
    {
        BranchCode = branchCode,
        DocumentNo = documentNo,
        Status = status,
        ApprovalStatus = approvalStatus,
        SupplierNameSnapshot = $"Supplier {documentNo}",
        CreatedDate = timestamp,
        UpdatedDate = timestamp,
        TargetWarehouseId = warehouseId,
        ReceivingLocationId = 1
    };

    private static GoodsReceiptTask GoodsReceiptTask(
        string branchCode,
        long userId,
        GoodsReceiptTaskStatus status = GoodsReceiptTaskStatus.Assigned) => new()
    {
        BranchCode = branchCode,
        TaskNo = Guid.NewGuid().ToString("N"),
        Status = status,
        Assignments =
        [
            new GoodsReceiptTaskAssignment
            {
                BranchCode = branchCode,
                UserId = userId,
                Status = status == GoodsReceiptTaskStatus.Completed
                    ? GoodsReceiptAssignmentStatus.Completed
                    : GoodsReceiptAssignmentStatus.Assigned,
                AssignedAtUtc = DateTimeOffset.UtcNow
            }
        ]
    };

    private static ShipmentHeader Shipment(
        string branchCode,
        string documentNo,
        DateTime timestamp,
        ShipmentStatus status = ShipmentStatus.Released,
        OperationApprovalStatus approvalStatus = OperationApprovalStatus.NotRequired,
        long warehouseId = 1) => new()
    {
        BranchCode = branchCode,
        DocumentNo = documentNo,
        CustomerCodeSnapshot = $"C-{documentNo}",
        Status = status,
        ApprovalStatus = approvalStatus,
        CreatedDate = timestamp,
        UpdatedDate = timestamp,
        SourceWarehouseId = warehouseId
    };

    private static ShipmentTask ShipmentTask(
        string branchCode,
        long userId,
        ShipmentTaskStatus status = ShipmentTaskStatus.Assigned) => new()
    {
        BranchCode = branchCode,
        TaskNo = Guid.NewGuid().ToString("N"),
        Status = status,
        Assignments =
        [
            new ShipmentTaskAssignment
            {
                BranchCode = branchCode,
                UserId = userId,
                AssignedAtUtc = DateTimeOffset.UtcNow
            }
        ]
    };

    private static WarehouseTransferHeader Transfer(
        string branchCode,
        WarehouseTransferStatus status,
        WarehouseTransferBusinessContext businessContext = WarehouseTransferBusinessContext.InterWarehouse,
        string? documentNo = null) => new()
    {
        BranchCode = branchCode,
        DocumentNo = documentNo ?? Guid.NewGuid().ToString("N"),
        Status = status,
        BusinessContext = businessContext
    };
}
