namespace verii_wms_api_v2.Modules.Dashboard.Application;

public sealed record DashboardActivity(
    string Id,
    string Kind,
    string Title,
    string Subtitle,
    string Timestamp,
    string Status);

public sealed record DashboardDailyOperationPoint(
    string Date,
    int GoodsReceiptCount,
    int ShipmentCount,
    int TransferCount);

public sealed record DashboardInventoryHealth(
    int AvailablePositionCount,
    int ReservedPositionCount,
    int QualityHoldPositionCount,
    int UnavailablePositionCount);

public sealed record DashboardSystemHealth(
    string GeneratedAtUtc,
    string? LastBalanceProjectionAtUtc,
    int ErpIssueCount);

public sealed record DashboardSummary(
    int StockItemCount,
    int GoodsReceiptOrderCount,
    int ShipmentOrderCount,
    int PendingGoodsReceiptApprovalCount,
    int MyAssignedTaskCount,
    int ActiveTransferOrderCount,
    int GoodsReceiptTodayCount,
    int ShipmentTodayCount,
    int TransferTodayCount,
    int PendingQualityInspectionCount,
    int OpenOperationCount,
    DashboardInventoryHealth InventoryHealth,
    IReadOnlyList<DashboardDailyOperationPoint> DailyOperations,
    DashboardSystemHealth SystemHealth,
    IReadOnlyList<DashboardActivity> RecentActivities);

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(
        long currentUserId,
        string? branchCode,
        CancellationToken cancellationToken = default);
}
