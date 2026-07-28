using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Dashboard.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Shipping.Domain;
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

    private static WarehouseStockBalance WarehouseBalance(string branchCode, long stockId) => new()
    {
        BranchCode = branchCode,
        DimensionKey = $"{branchCode}:{stockId}",
        WarehouseId = 1,
        StockId = stockId,
        LastTransactionDate = DateTime.UtcNow
    };

    private static GoodsReceiptHeader GoodsReceipt(
        string branchCode,
        string documentNo,
        DateTime timestamp,
        WarehouseOperationStatus status = WarehouseOperationStatus.Released,
        OperationApprovalStatus approvalStatus = OperationApprovalStatus.NotRequired) => new()
    {
        BranchCode = branchCode,
        DocumentNo = documentNo,
        Status = status,
        ApprovalStatus = approvalStatus,
        SupplierNameSnapshot = $"Supplier {documentNo}",
        CreatedDate = timestamp,
        UpdatedDate = timestamp,
        TargetWarehouseId = 1,
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
        OperationApprovalStatus approvalStatus = OperationApprovalStatus.NotRequired) => new()
    {
        BranchCode = branchCode,
        DocumentNo = documentNo,
        CustomerCodeSnapshot = $"C-{documentNo}",
        Status = status,
        ApprovalStatus = approvalStatus,
        CreatedDate = timestamp,
        UpdatedDate = timestamp
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
        WarehouseTransferBusinessContext businessContext = WarehouseTransferBusinessContext.InterWarehouse) => new()
    {
        BranchCode = branchCode,
        DocumentNo = Guid.NewGuid().ToString("N"),
        Status = status,
        BusinessContext = businessContext
    };
}
