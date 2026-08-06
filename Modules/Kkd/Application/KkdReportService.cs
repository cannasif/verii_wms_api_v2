using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdValidationLogRow(
    long Id, Guid CorrelationId, long? EmployeeId, long? StockId, string? GroupCode,
    long? WarehouseId, decimal AttemptedQuantity, string ReasonCode, string? Message,
    string? DeviceInfo, DateTime? CreatedDate);

public sealed record KkdUsageSummaryRow(
    string Code, string Name, int DistributionCount, int EmployeeCount,
    decimal DeliveredQuantity, decimal EntitledQuantity, decimal ExcessQuantity);

public sealed record KkdRemainingEntitlementRow(
    long EmployeeId, string EmployeeCode, string EmployeeName, string GroupCode, string GroupName,
    long StockId, string StockCode, string StockName, string? PhaseType,
    decimal MatrixRemainingQuantity, decimal OverrideRemainingQuantity, decimal TotalRemainingQuantity,
    DateTimeOffset? LastUsageAtUtc, DateOnly? NextEligibleDate, string ReasonCode, string Message);

public interface IKkdReportService
{
    Task<IReadOnlyList<KkdRemainingEntitlementRow>> GetRemainingEntitlementsAsync(
        long employeeId, DateOnly? atDate, CancellationToken ct = default);
    Task<IReadOnlyList<KkdValidationLogRow>> GetValidationLogsAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<KkdUsageSummaryRow>> GetUsageAsync(string dimension, DateOnly? from, DateOnly? to,
        CancellationToken ct = default);
}

public sealed class KkdReportService(IUnitOfWork uow, IKkdEntitlementService entitlements) : IKkdReportService
{
    public async Task<IReadOnlyList<KkdRemainingEntitlementRow>> GetRemainingEntitlementsAsync(
        long employeeId, DateOnly? atDate, CancellationToken ct = default)
    {
        var date = atDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var employee = await uow.Repository<KkdEmployee>().Query()
            .Where(x => x.Id == employeeId && x.IsActive)
            .Select(x => new
            {
                x.Id, x.EmployeeCode, EmployeeName = x.FirstName + " " + x.LastName,
                x.CustomerId, x.DepartmentId, x.RoleId
            })
            .SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("Aktif KKD personeli bulunamadı.");

        var ruleCandidates = await uow.Repository<KkdEntitlementRule>().Query()
            .Where(x => x.IsActive && x.Matrix.IsActive
                && x.Matrix.CustomerId == employee.CustomerId
                && x.Matrix.DepartmentId == employee.DepartmentId
                && x.Matrix.RoleId == employee.RoleId
                && (!x.Matrix.EffectiveFrom.HasValue || x.Matrix.EffectiveFrom <= date)
                && (!x.Matrix.EffectiveTo.HasValue || x.Matrix.EffectiveTo >= date))
            .Select(x => new { x.GroupCode, GroupName = x.GroupName ?? x.GroupCode, x.StockId, x.SortOrder })
            .ToListAsync(ct);
        var overrideGroups = await uow.Repository<KkdEmployeeEntitlementOverride>().Query()
            .Where(x => x.EmployeeId == employee.Id && x.IsActive
                && x.ValidFrom <= date && (!x.ValidTo.HasValue || x.ValidTo >= date))
            .Select(x => x.GroupCode)
            .Distinct()
            .ToListAsync(ct);

        var groupCodes = ruleCandidates.Select(x => x.GroupCode)
            .Concat(overrideGroups)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (groupCodes.Length == 0) return [];

        var representativeStocks = await uow.Repository<StockEntity>().Query()
            .Where(x => x.GroupCode != null && groupCodes.Contains(x.GroupCode))
            .OrderBy(x => x.ErpStockCode)
            .Select(x => new { x.Id, x.ErpStockCode, x.StockName, x.GroupCode })
            .ToListAsync(ct);
        var stockSpecificIds = ruleCandidates.Where(x => x.StockId.HasValue).Select(x => x.StockId!.Value).Distinct().ToArray();
        var stockSpecific = stockSpecificIds.Length == 0
            ? []
            : await uow.Repository<StockEntity>().Query()
                .Where(x => stockSpecificIds.Contains(x.Id))
                .Select(x => new { x.Id, x.ErpStockCode, x.StockName, x.GroupCode })
                .ToListAsync(ct);

        var rows = new List<KkdRemainingEntitlementRow>();
        foreach (var groupCode in groupCodes.OrderBy(x => x))
        {
            var groupRules = ruleCandidates.Where(x => string.Equals(x.GroupCode, groupCode, StringComparison.OrdinalIgnoreCase)).ToArray();
            var preferredId = groupRules.Where(x => x.StockId.HasValue).OrderBy(x => x.SortOrder).Select(x => x.StockId).FirstOrDefault();
            var stock = preferredId.HasValue
                ? stockSpecific.FirstOrDefault(x => x.Id == preferredId.Value)
                : representativeStocks.FirstOrDefault(x => string.Equals(x.GroupCode, groupCode, StringComparison.OrdinalIgnoreCase));
            if (stock is null) continue;

            var result = await entitlements.CheckAsync(new(employee.Id, stock.Id, 1, date), ct);
            var lastUsage = await uow.Repository<KkdEntitlementConsumption>().Query()
                .Where(x => x.EmployeeId == employee.Id && x.GroupCode == groupCode && !x.IsReversal)
                .OrderByDescending(x => x.ConsumedAtUtc)
                .Select(x => (DateTimeOffset?)x.ConsumedAtUtc)
                .FirstOrDefaultAsync(ct);
            rows.Add(new(employee.Id, employee.EmployeeCode, employee.EmployeeName.Trim(), groupCode,
                groupRules.Select(x => x.GroupName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? groupCode,
                stock.Id, stock.ErpStockCode, stock.StockName, result.PhaseType,
                result.MatrixRemainingQuantity, result.OverrideRemainingQuantity, result.TotalRemainingQuantity,
                lastUsage, result.NextEligibleDate, result.ReasonCode, result.Message));
        }
        return rows;
    }

    public async Task<IReadOnlyList<KkdValidationLogRow>> GetValidationLogsAsync(int take, CancellationToken ct = default)
    {
        var limit = Math.Clamp(take, 1, 1000);
        return await uow.Repository<KkdValidationLog>().Query()
            .OrderByDescending(x => x.Id).Take(limit)
            .Select(x => new KkdValidationLogRow(x.Id, x.CorrelationId, x.EmployeeId, x.StockId, x.GroupCode,
                x.WarehouseId, x.AttemptedQuantity, x.ReasonCode, x.Message, x.DeviceInfo, x.CreatedDate))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KkdUsageSummaryRow>> GetUsageAsync(
        string dimension, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && to < from)
            throw AppException.BadRequest("KKD rapor bitiş tarihi başlangıç tarihinden önce olamaz.");
        var fromUtc = from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusive = to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = uow.Repository<KkdDistributionLine>().Query()
            .Where(x => x.Distribution.Status == KkdDistributionStatus.Completed
                && (!fromUtc.HasValue || x.Distribution.CompletedAtUtc >= fromUtc.Value)
                && (!toExclusive.HasValue || x.Distribution.CompletedAtUtc < toExclusive.Value));

        var normalized = dimension.Trim().ToUpperInvariant();
        if (normalized == "DEPARTMENT")
            return await query.GroupBy(x => new
                {
                    x.Distribution.Employee.Department.Code,
                    x.Distribution.Employee.Department.Name
                })
                .Select(x => new KkdUsageSummaryRow(x.Key.Code, x.Key.Name,
                    x.Select(y => y.DistributionId).Distinct().Count(),
                    x.Select(y => y.Distribution.EmployeeId).Distinct().Count(),
                    x.Sum(y => y.Quantity), x.Sum(y => y.EntitledQuantity), x.Sum(y => y.ExcessQuantity)))
                .OrderByDescending(x => x.DeliveredQuantity).ToListAsync(ct);
        if (normalized == "ROLE")
            return await query.GroupBy(x => new
                {
                    x.Distribution.Employee.Role.Code,
                    x.Distribution.Employee.Role.Name
                })
                .Select(x => new KkdUsageSummaryRow(x.Key.Code, x.Key.Name,
                    x.Select(y => y.DistributionId).Distinct().Count(),
                    x.Select(y => y.Distribution.EmployeeId).Distinct().Count(),
                    x.Sum(y => y.Quantity), x.Sum(y => y.EntitledQuantity), x.Sum(y => y.ExcessQuantity)))
                .OrderByDescending(x => x.DeliveredQuantity).ToListAsync(ct);
        if (normalized == "GROUP")
            return await query.GroupBy(x => x.GroupCode)
                .Select(x => new KkdUsageSummaryRow(x.Key, x.Key,
                    x.Select(y => y.DistributionId).Distinct().Count(),
                    x.Select(y => y.Distribution.EmployeeId).Distinct().Count(),
                    x.Sum(y => y.Quantity), x.Sum(y => y.EntitledQuantity), x.Sum(y => y.ExcessQuantity)))
                .OrderByDescending(x => x.DeliveredQuantity).ToListAsync(ct);

        throw AppException.BadRequest("KKD rapor kırılımı Department, Role veya Group olmalıdır.");
    }
}
