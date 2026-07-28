using System.Globalization;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Dashboard.Application;

public sealed class DashboardService(WmsDbContext dbContext) : IDashboardService
{
    private const int RecentActivityLimit = 8;

    public async Task<DashboardSummary> GetSummaryAsync(
        long currentUserId,
        string? branchCode,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId <= 0)
            throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");

        var branch = NormalizeBranchCode(branchCode);

        var stockItemCount = await dbContext.WarehouseStockBalances
            .CountAsync(x => x.BranchCode == branch, cancellationToken);
        var goodsReceiptOrderCount = await dbContext.GoodsReceiptHeaders
            .CountAsync(x => x.BranchCode == branch, cancellationToken);
        var shipmentOrderCount = await dbContext.ShipmentHeaders
            .CountAsync(x => x.BranchCode == branch, cancellationToken);
        var pendingGoodsReceiptApprovalCount = await dbContext.GoodsReceiptHeaders
            .CountAsync(
                x => x.BranchCode == branch
                    && x.ApprovalStatus == OperationApprovalStatus.Pending
                    && x.Status != WarehouseOperationStatus.Cancelled,
                cancellationToken);

        var assignedGoodsReceiptTaskCount = await dbContext.GoodsReceiptTasks
            .CountAsync(
                task => task.BranchCode == branch
                    && task.Status != GoodsReceiptTaskStatus.Completed
                    && task.Status != GoodsReceiptTaskStatus.Cancelled
                    && task.Assignments.Any(
                        assignment => assignment.UserId == currentUserId
                            && assignment.Status != GoodsReceiptAssignmentStatus.Completed
                            && assignment.Status != GoodsReceiptAssignmentStatus.Unassigned
                            && assignment.Status != GoodsReceiptAssignmentStatus.Rejected),
                cancellationToken);
        var assignedShipmentTaskCount = await dbContext.ShipmentTasks
            .CountAsync(
                task => task.BranchCode == branch
                    && task.Status != ShipmentTaskStatus.Completed
                    && task.Status != ShipmentTaskStatus.Cancelled
                    && task.Assignments.Any(assignment => assignment.UserId == currentUserId),
                cancellationToken);

        var activeTransferOrderCount = await dbContext.WarehouseTransferHeaders
            .CountAsync(
                x => x.BranchCode == branch
                    && x.BusinessContext == WarehouseTransferBusinessContext.InterWarehouse
                    && x.Status != WarehouseTransferStatus.Completed
                    && x.Status != WarehouseTransferStatus.Cancelled,
                cancellationToken);

        var recentGoodsReceipts = await dbContext.GoodsReceiptHeaders
            .Where(x => x.BranchCode == branch && (x.UpdatedDate != null || x.CreatedDate != null))
            .OrderByDescending(x => x.UpdatedDate ?? x.CreatedDate)
            .Take(RecentActivityLimit)
            .Select(x => new
            {
                x.Id,
                x.DocumentNo,
                x.SupplierNameSnapshot,
                x.SupplierCodeSnapshot,
                x.Status,
                x.ApprovalStatus,
                Timestamp = x.UpdatedDate ?? x.CreatedDate
            })
            .ToListAsync(cancellationToken);

        var recentShipments = await dbContext.ShipmentHeaders
            .Where(x => x.BranchCode == branch && (x.UpdatedDate != null || x.CreatedDate != null))
            .OrderByDescending(x => x.UpdatedDate ?? x.CreatedDate)
            .Take(RecentActivityLimit)
            .Select(x => new
            {
                x.Id,
                x.DocumentNo,
                x.CustomerNameSnapshot,
                x.CustomerCodeSnapshot,
                x.Status,
                x.ApprovalStatus,
                Timestamp = x.UpdatedDate ?? x.CreatedDate
            })
            .ToListAsync(cancellationToken);

        var recentActivities = recentGoodsReceipts
            .Select(x => new DashboardActivityCandidate(
                $"gr-{x.Id}",
                "goods-receipt",
                DisplayTitle(x.DocumentNo, x.Id),
                DisplaySubtitle(x.SupplierNameSnapshot, x.SupplierCodeSnapshot),
                x.Timestamp!.Value,
                GoodsReceiptActivityStatus(x.Status, x.ApprovalStatus)))
            .Concat(recentShipments.Select(x => new DashboardActivityCandidate(
                $"sh-{x.Id}",
                "shipment",
                DisplayTitle(x.DocumentNo, x.Id),
                DisplaySubtitle(x.CustomerNameSnapshot, x.CustomerCodeSnapshot),
                x.Timestamp!.Value,
                ShipmentActivityStatus(x.Status, x.ApprovalStatus))))
            .OrderByDescending(x => x.Timestamp)
            .Take(RecentActivityLimit)
            .Select(x => new DashboardActivity(
                x.Id,
                x.Kind,
                x.Title,
                x.Subtitle,
                ToUtcIso8601(x.Timestamp),
                x.Status))
            .ToList();

        return new DashboardSummary(
            stockItemCount,
            goodsReceiptOrderCount,
            shipmentOrderCount,
            pendingGoodsReceiptApprovalCount,
            assignedGoodsReceiptTaskCount + assignedShipmentTaskCount,
            activeTransferOrderCount,
            recentActivities);
    }

    internal static string NormalizeBranchCode(string? branchCode)
    {
        var normalized = string.IsNullOrWhiteSpace(branchCode) ? "0" : branchCode.Trim();
        if (normalized.Length > 10)
            throw AppException.BadRequest("Şube kodu en fazla 10 karakter olabilir.");
        return normalized;
    }

    internal static string GoodsReceiptActivityStatus(
        WarehouseOperationStatus status,
        OperationApprovalStatus approvalStatus) =>
        status == WarehouseOperationStatus.Completed
            ? "completed"
            : approvalStatus == OperationApprovalStatus.Pending
                ? "pending"
                : "preparing";

    internal static string ShipmentActivityStatus(
        ShipmentStatus status,
        OperationApprovalStatus approvalStatus) =>
        status == ShipmentStatus.Shipped
            ? "completed"
            : status == ShipmentStatus.AwaitingApproval || approvalStatus == OperationApprovalStatus.Pending
                ? "pending"
                : "preparing";

    private static string DisplayTitle(string? documentNo, long id) =>
        string.IsNullOrWhiteSpace(documentNo) ? $"#{id}" : documentNo.Trim();

    private static string DisplaySubtitle(string? name, string? code) =>
        !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : !string.IsNullOrWhiteSpace(code)
                ? code.Trim()
                : "-";

    private static string ToUtcIso8601(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return utc.ToString("O", CultureInfo.InvariantCulture);
    }

    private sealed record DashboardActivityCandidate(
        string Id,
        string Kind,
        string Title,
        string Subtitle,
        DateTime Timestamp,
        string Status);
}
