namespace verii_wms_api_v2.Modules.Dashboard.Application;

public sealed record DashboardActivity(
    string Id,
    string Kind,
    string Title,
    string Subtitle,
    string Timestamp,
    string Status);

public sealed record DashboardSummary(
    int StockItemCount,
    int GoodsReceiptOrderCount,
    int ShipmentOrderCount,
    int PendingGoodsReceiptApprovalCount,
    int MyAssignedTaskCount,
    int ActiveTransferOrderCount,
    IReadOnlyList<DashboardActivity> RecentActivities);

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(
        long currentUserId,
        string? branchCode,
        CancellationToken cancellationToken = default);
}
