using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdValidationLogRow(
    long Id, Guid CorrelationId, long? EmployeeId, long? StockId, string? GroupCode,
    long? WarehouseId, decimal AttemptedQuantity, string ReasonCode, string? Message,
    string? DeviceInfo, DateTime? CreatedDate);

public sealed record KkdUsageSummaryRow(
    string Code, string Name, int DistributionCount, int EmployeeCount,
    decimal DeliveredQuantity, decimal EntitledQuantity, decimal ExcessQuantity);

public interface IKkdReportService
{
    Task<IReadOnlyList<KkdValidationLogRow>> GetValidationLogsAsync(int take, CancellationToken ct = default);
    Task<IReadOnlyList<KkdUsageSummaryRow>> GetUsageAsync(string dimension, DateOnly? from, DateOnly? to,
        CancellationToken ct = default);
}

public sealed class KkdReportService(IUnitOfWork uow) : IKkdReportService
{
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
