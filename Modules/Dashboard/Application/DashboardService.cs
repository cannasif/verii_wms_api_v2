using System.Globalization;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Dashboard.Application;

public sealed class DashboardService(WmsDbContext dbContext) : IDashboardService
{
    private const int RecentActivityLimit = 8;
    private const int TrendDayCount = 7;

    public async Task<DashboardSummary> GetSummaryAsync(
        long currentUserId,
        string? branchCode,
        CancellationToken cancellationToken = default)
    {
        if (currentUserId <= 0)
            throw AppException.Unauthorized("Geçersiz kullanıcı oturumu.");

        var branch = NormalizeBranchCode(branchCode);
        var generatedAtUtc = DateTime.UtcNow;
        var timeZone = await ResolveTimeZoneAsync(branch, cancellationToken);
        var businessToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(generatedAtUtc, timeZone));
        var trendFirstDay = businessToday.AddDays(-(TrendDayCount - 1));
        var trendStartUtc = ToUtcStartOfDay(trendFirstDay, timeZone);
        var trendEndUtc = ToUtcStartOfDay(businessToday.AddDays(1), timeZone);

        var stockItemCount = await dbContext.WarehouseStockBalances
            .AsNoTracking()
            .Where(x => x.BranchCode == branch)
            .Select(x => x.StockId)
            .Distinct()
            .CountAsync(cancellationToken);

        var goodsReceiptAggregate = await dbContext.GoodsReceiptHeaders
            .AsNoTracking()
            .Where(x => x.BranchCode == branch)
            .GroupBy(_ => 1)
            .Select(group => new DocumentAggregate(
                group.Count(),
                group.Count(x => x.Status != WarehouseOperationStatus.Completed
                    && x.Status != WarehouseOperationStatus.Cancelled),
                group.Count(x => x.ApprovalStatus == OperationApprovalStatus.Pending
                    && x.Status != WarehouseOperationStatus.Cancelled),
                group.Count(x => x.ErpIntegrationStatus == ErpIntegrationStatus.Failed
                    || x.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? DocumentAggregate.Empty;

        var shipmentAggregate = await dbContext.ShipmentHeaders
            .AsNoTracking()
            .Where(x => x.BranchCode == branch)
            .GroupBy(_ => 1)
            .Select(group => new DocumentAggregate(
                group.Count(),
                group.Count(x => x.Status != ShipmentStatus.Shipped
                    && x.Status != ShipmentStatus.Cancelled),
                group.Count(x => x.ApprovalStatus == OperationApprovalStatus.Pending
                    && x.Status != ShipmentStatus.Cancelled),
                group.Count(x => x.ErpIntegrationStatus == ErpIntegrationStatus.Failed
                    || x.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? DocumentAggregate.Empty;

        var transferAggregate = await dbContext.WarehouseTransferHeaders
            .AsNoTracking()
            .Where(x => x.BranchCode == branch)
            .GroupBy(_ => 1)
            .Select(group => new TransferAggregate(
                group.Count(x => x.BusinessContext == WarehouseTransferBusinessContext.InterWarehouse
                    && x.Status != WarehouseTransferStatus.Completed
                    && x.Status != WarehouseTransferStatus.CompletedWithShortage
                    && x.Status != WarehouseTransferStatus.Cancelled),
                group.Count(x => x.Status != WarehouseTransferStatus.Completed
                    && x.Status != WarehouseTransferStatus.CompletedWithShortage
                    && x.Status != WarehouseTransferStatus.Cancelled),
                group.Count(x => x.ErpIntegrationStatus == ErpIntegrationStatus.Failed
                    || x.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? TransferAggregate.Empty;

        var pendingQualityInspectionCount = await dbContext.QualityInspections
            .AsNoTracking()
            .CountAsync(
                x => x.BranchCode == branch
                    && (x.Status == QualityInspectionStatus.Pending
                        || x.Status == QualityInspectionStatus.InProgress
                        || x.Status == QualityInspectionStatus.PartiallyDecided),
                cancellationToken);

        var assignedGoodsReceiptTaskCount = await dbContext.GoodsReceiptTasks
            .AsNoTracking()
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
            .AsNoTracking()
            .CountAsync(
                task => task.BranchCode == branch
                    && task.Status != ShipmentTaskStatus.Completed
                    && task.Status != ShipmentTaskStatus.Cancelled
                    && task.Assignments.Any(assignment => assignment.UserId == currentUserId),
                cancellationToken);

        var goodsReceiptDates = await ReadTrendDatesAsync(
            dbContext.GoodsReceiptHeaders
                .AsNoTracking()
                .Where(x => x.BranchCode == branch)
                .Select(x => x.CreatedDate),
            trendStartUtc,
            trendEndUtc,
            cancellationToken);
        var shipmentDates = await ReadTrendDatesAsync(
            dbContext.ShipmentHeaders
                .AsNoTracking()
                .Where(x => x.BranchCode == branch)
                .Select(x => x.CreatedDate),
            trendStartUtc,
            trendEndUtc,
            cancellationToken);
        var transferDates = await ReadTrendDatesAsync(
            dbContext.WarehouseTransferHeaders
                .AsNoTracking()
                .Where(x => x.BranchCode == branch)
                .Select(x => x.CreatedDate),
            trendStartUtc,
            trendEndUtc,
            cancellationToken);

        var goodsReceiptTrend = CountByBusinessDate(goodsReceiptDates, timeZone);
        var shipmentTrend = CountByBusinessDate(shipmentDates, timeZone);
        var transferTrend = CountByBusinessDate(transferDates, timeZone);
        var dailyOperations = Enumerable.Range(0, TrendDayCount)
            .Select(offset => trendFirstDay.AddDays(offset))
            .Select(date => new DashboardDailyOperationPoint(
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                goodsReceiptTrend.GetValueOrDefault(date),
                shipmentTrend.GetValueOrDefault(date),
                transferTrend.GetValueOrDefault(date)))
            .ToList();

        var inventoryHealth = await BuildInventoryHealthAsync(branch, cancellationToken);
        var lastBalanceProjectionAtUtc = await dbContext.StockBalanceProjectionStates
            .AsNoTracking()
            .Where(x => x.ProjectionName == StockBalanceProjectionNames.Current)
            .Select(x => x.LastProjectedAt)
            .SingleOrDefaultAsync(cancellationToken);

        var recentGoodsReceipts = await dbContext.GoodsReceiptHeaders
            .AsNoTracking()
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
            .AsNoTracking()
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

        var recentTransfers = await dbContext.WarehouseTransferHeaders
            .AsNoTracking()
            .Where(x => x.BranchCode == branch && (x.UpdatedDate != null || x.CreatedDate != null))
            .OrderByDescending(x => x.UpdatedDate ?? x.CreatedDate)
            .Take(RecentActivityLimit)
            .Select(x => new
            {
                x.Id,
                x.DocumentNo,
                SourceWarehouseName = dbContext.Warehouses
                    .Where(warehouse => warehouse.Id == x.SourceWarehouseId)
                    .Select(warehouse => warehouse.WarehouseName)
                    .FirstOrDefault(),
                TargetWarehouseName = dbContext.Warehouses
                    .Where(warehouse => warehouse.Id == x.TargetWarehouseId)
                    .Select(warehouse => warehouse.WarehouseName)
                    .FirstOrDefault(),
                x.Status,
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
            .Concat(recentTransfers.Select(x => new DashboardActivityCandidate(
                $"tr-{x.Id}",
                "transfer",
                DisplayTitle(x.DocumentNo, x.Id),
                WarehouseRouteSubtitle(x.SourceWarehouseName, x.TargetWarehouseName),
                x.Timestamp!.Value,
                TransferActivityStatus(x.Status))))
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
            goodsReceiptAggregate.TotalCount,
            shipmentAggregate.TotalCount,
            goodsReceiptAggregate.PendingApprovalCount,
            assignedGoodsReceiptTaskCount + assignedShipmentTaskCount,
            transferAggregate.ActiveInterWarehouseCount,
            goodsReceiptTrend.GetValueOrDefault(businessToday),
            shipmentTrend.GetValueOrDefault(businessToday),
            transferTrend.GetValueOrDefault(businessToday),
            pendingQualityInspectionCount,
            goodsReceiptAggregate.OpenCount
                + shipmentAggregate.OpenCount
                + transferAggregate.OpenCount
                + pendingQualityInspectionCount,
            inventoryHealth,
            dailyOperations,
            new DashboardSystemHealth(
                ToUtcIso8601(generatedAtUtc),
                lastBalanceProjectionAtUtc.HasValue
                    ? ToUtcIso8601(lastBalanceProjectionAtUtc.Value)
                    : null,
                goodsReceiptAggregate.ErpIssueCount
                    + shipmentAggregate.ErpIssueCount
                    + transferAggregate.ErpIssueCount),
            recentActivities);
    }

    private async Task<TimeZoneInfo> ResolveTimeZoneAsync(
        string branch,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await dbContext.ProjectSettings
            .AsNoTracking()
            .Where(x => x.BranchCode == branch || x.BranchCode == "0")
            .OrderByDescending(x => x.BranchCode == branch)
            .Select(x => x.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static Task<List<DateTime>> ReadTrendDatesAsync(
        IQueryable<DateTime?> query,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken) =>
        query
            .Where(value => value >= startUtc && value < endUtc)
            .Select(value => value!.Value)
            .ToListAsync(cancellationToken);

    private async Task<DashboardInventoryHealth> BuildInventoryHealthAsync(
        string branch,
        CancellationToken cancellationToken)
    {
        var aggregate = await dbContext.WarehouseStockBalances
            .AsNoTracking()
            .Where(x => x.BranchCode == branch && x.Quantity > 0)
            .GroupBy(_ => 1)
            .Select(group => new DashboardInventoryHealth(
                group.Count(x => x.StockStatus == "Available" && x.AvailableQuantity > 0),
                group.Count(x => x.StockStatus == "Available"
                    && x.AvailableQuantity <= 0
                    && x.ReservedQuantity > 0),
                group.Count(x => x.StockStatus == "QualityPending"
                    || x.StockStatus == "Quarantine"
                    || x.StockStatus == "Rejected"),
                group.Count(x => (x.StockStatus == "Available"
                        && x.AvailableQuantity <= 0
                        && x.ReservedQuantity <= 0)
                    || (x.StockStatus != "Available"
                        && x.StockStatus != "QualityPending"
                        && x.StockStatus != "Quarantine"
                        && x.StockStatus != "Rejected"))))
            .SingleOrDefaultAsync(cancellationToken);

        return aggregate ?? new DashboardInventoryHealth(0, 0, 0, 0);
    }

    private static Dictionary<DateOnly, int> CountByBusinessDate(
        IEnumerable<DateTime> timestamps,
        TimeZoneInfo timeZone) =>
        timestamps
            .Select(EnsureUtc)
            .Select(timestamp => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(timestamp, timeZone)))
            .GroupBy(date => date)
            .ToDictionary(group => group.Key, group => group.Count());

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime ToUtcStartOfDay(DateOnly date, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            timeZone);

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

    internal static string TransferActivityStatus(WarehouseTransferStatus status) =>
        status == WarehouseTransferStatus.Completed
            || status == WarehouseTransferStatus.CompletedWithShortage
            ? "completed"
            : status == WarehouseTransferStatus.Draft
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

    private static string WarehouseRouteSubtitle(string? sourceCode, string? targetCode) =>
        $"{DisplaySubtitle(null, sourceCode)} → {DisplaySubtitle(null, targetCode)}";

    private static string ToUtcIso8601(DateTime value) =>
        EnsureUtc(value).ToString("O", CultureInfo.InvariantCulture);

    private sealed record DashboardActivityCandidate(
        string Id,
        string Kind,
        string Title,
        string Subtitle,
        DateTime Timestamp,
        string Status);

    private sealed record DocumentAggregate(
        int TotalCount,
        int OpenCount,
        int PendingApprovalCount,
        int ErpIssueCount)
    {
        public static readonly DocumentAggregate Empty = new(0, 0, 0, 0);
    }

    private sealed record TransferAggregate(
        int ActiveInterWarehouseCount,
        int OpenCount,
        int ErpIssueCount)
    {
        public static readonly TransferAggregate Empty = new(0, 0, 0);
    }
}
