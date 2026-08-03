using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed class KkdEntitlementService(IUnitOfWork uow) : IKkdEntitlementService
{
    public async Task<KkdEntitlementCheckResult> CheckAsync(KkdEntitlementCheckRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0) throw AppException.BadRequest("KKD miktarı sıfırdan büyük olmalıdır.");
        var date = request.AtDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var employee = await uow.Repository<KkdEmployee>().Query()
            .SingleOrDefaultAsync(x => x.Id == request.EmployeeId, cancellationToken)
            ?? throw AppException.NotFound("KKD personeli bulunamadı.");
        if (!employee.IsActive) return Denied(request, employee.Id, string.Empty, "EMPLOYEE_INACTIVE", "Personel aktif değil.");
        if (date < employee.EmploymentStartDate) return Denied(request, employee.Id, string.Empty, "EMPLOYMENT_NOT_STARTED", "Personelin işe giriş tarihi henüz gelmedi.");

        var stock = await uow.Repository<StockEntity>().Query().SingleOrDefaultAsync(x => x.Id == request.StockId, cancellationToken)
            ?? throw AppException.NotFound("Stok bulunamadı.");
        var groupCode = Normalize(stock.GroupCode);
        if (groupCode.Length == 0) return Denied(request, employee.Id, groupCode, "STOCK_GROUP_MISSING", "Stok kartında KKD grup kodu bulunmuyor.");

        var candidates = await uow.Repository<KkdEntitlementRule>().Query()
            .Include(x => x.Matrix)
            .Include(x => x.Phases)
            .Where(x => x.IsActive
                && x.Matrix.IsActive
                && x.Matrix.CustomerId == employee.CustomerId
                && x.Matrix.DepartmentId == employee.DepartmentId
                && x.Matrix.RoleId == employee.RoleId
                && (!x.Matrix.EffectiveFrom.HasValue || x.Matrix.EffectiveFrom <= date)
                && (!x.Matrix.EffectiveTo.HasValue || x.Matrix.EffectiveTo >= date)
                && (x.StockId == stock.Id || (x.StockId == null && x.GroupCode == groupCode)))
            .ToListAsync(cancellationToken);

        // Stok özel tanım, grup tanımından daima daha belirleyicidir.
        var rule = candidates
            .OrderByDescending(x => x.StockId == stock.Id)
            .ThenByDescending(x => x.Matrix.EffectiveFrom)
            .ThenBy(x => x.SortOrder)
            .FirstOrDefault();

        var matrixRemaining = 0m;
        KkdEntitlementPhase? phase = null;
        DateOnly periodStart = date;
        DateOnly? periodEnd = null;
        DateOnly? nextEligible = null;
        var allocations = new List<KkdEntitlementAllocation>();
        var matrixRequestAllowed = true;

        if (rule is not null)
        {
            var phaseWindow = ResolvePhaseWindow(employee.EmploymentStartDate, date, rule.Phases.Where(x => x.IsActive).ToArray());
            phase = phaseWindow.Phase;
            periodStart = phaseWindow.Start;
            periodEnd = phaseWindow.End;
            nextEligible = phaseWindow.NextEligible;

            if (phase is not null)
            {
                var consumptions = await uow.Repository<KkdEntitlementConsumption>().Query()
                    .Where(x => x.EmployeeId == employee.Id && x.RuleId == rule.Id && x.SourceType == KkdEntitlementSourceType.Matrix)
                    .ToListAsync(cancellationToken);
                var periodConsumed = NetConsumed(consumptions.Where(x => DateOnly.FromDateTime(x.ConsumedAtUtc.UtcDateTime) >= periodStart
                    && (!periodEnd.HasValue || DateOnly.FromDateTime(x.ConsumedAtUtc.UtcDateTime) <= periodEnd.Value)));
                var periodReserved = await ReservedAsync(
                    KkdEntitlementSourceType.Matrix, phase.Id, periodStart, periodEnd, cancellationToken);
                var allowed = phase.Quantity;

                if (phase.PhaseType == KkdEntitlementPhaseType.Recurring && rule.MaxCarryQuantity is > 0)
                    allowed += CalculateCarry(employee.EmploymentStartDate, phase, rule.MaxCarryQuantity.Value, periodStart, consumptions);

                if (phase.FrequencyDays is > 0 && phase.QuantityPerFrequency is > 0)
                {
                    var windowStart = date.AddDays(-(phase.FrequencyDays.Value - 1));
                    var frequencyConsumed = NetConsumed(consumptions.Where(x => DateOnly.FromDateTime(x.ConsumedAtUtc.UtcDateTime) >= windowStart
                        && DateOnly.FromDateTime(x.ConsumedAtUtc.UtcDateTime) <= date));
                    var frequencyReserved = await ReservedBetweenAsync(phase.Id, windowStart, date, cancellationToken);
                    var frequencyRemaining = Math.Max(0, phase.QuantityPerFrequency.Value - frequencyConsumed - frequencyReserved);
                    allowed = Math.Min(allowed - periodConsumed - periodReserved, frequencyRemaining) + periodConsumed + periodReserved;
                    nextEligible ??= date.AddDays(phase.FrequencyDays.Value);
                }

                var annual = EmploymentYear(employee.EmploymentStartDate, date);
                var annualPhaseIds = rule.Phases.Where(x => x.IsActive).Select(x => x.Id).ToArray();
                var annualRows = consumptions.Where(x => DateOnly.FromDateTime(x.ConsumedAtUtc.UtcDateTime) >= annual.Start
                    && DateOnly.FromDateTime(x.ConsumedAtUtc.UtcDateTime) <= annual.End).ToArray();
                if (rule.AnnualQuantity is >= 0)
                {
                    var annualReserved = await ReservedBetweenAsync(annualPhaseIds, annual.Start, annual.End, cancellationToken);
                    var annualRemaining = Math.Max(0, rule.AnnualQuantity.Value - NetConsumed(annualRows) - annualReserved);
                    allowed = Math.Min(allowed - periodConsumed - periodReserved, annualRemaining) + periodConsumed + periodReserved;
                }
                if (rule.AnnualIssueCount is > 0)
                {
                    var issueCount = annualRows.Where(x => !x.IsReversal).Select(x => x.DistributionLineId).Distinct().Count();
                    var reservedIssueCount = await ReservedIssueCountAsync(annualPhaseIds, annual.Start, annual.End, cancellationToken);
                    if (issueCount + reservedIssueCount >= rule.AnnualIssueCount.Value)
                        allowed = periodConsumed + periodReserved;
                }

                matrixRemaining = Math.Max(0, allowed - periodConsumed - periodReserved);
                var bulkLimit = phase.FrequencyDays is > 0 && phase.QuantityPerFrequency is > 0
                    ? phase.QuantityPerFrequency.Value
                    : phase.Quantity;
                if ((!rule.AllowBulkIssue || !phase.AllowBulkIssue) && request.Quantity > bulkLimit)
                {
                    matrixRequestAllowed = false;
                    nextEligible ??= periodEnd?.AddDays(1);
                }
            }
        }

        var overrides = await uow.Repository<KkdEmployeeEntitlementOverride>().Query()
            .Where(x => x.EmployeeId == employee.Id && x.IsActive && x.GroupCode == groupCode
                && x.ValidFrom <= date && (!x.ValidTo.HasValue || x.ValidTo >= date))
            .OrderBy(x => x.ValidTo ?? DateOnly.MaxValue)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var overrideReserved = overrides.Count == 0
            ? new Dictionary<long, decimal>()
            : await uow.Repository<KkdDistributionEntitlementAllocation>().Query()
                .Where(x => x.SourceType == KkdEntitlementSourceType.ManualOverride
                    && overrides.Select(o => o.Id).Contains(x.SourceId)
                    && (x.DistributionLine.Distribution.Status == KkdDistributionStatus.Draft
                        || x.DistributionLine.Distribution.Status == KkdDistributionStatus.Validated
                        || x.DistributionLine.Distribution.Status == KkdDistributionStatus.OutboundCreated))
                .GroupBy(x => x.SourceId)
                .Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.Quantity) })
                .ToDictionaryAsync(x => x.Id, x => x.Quantity, cancellationToken);
        var overrideRemaining = overrides.Sum(x => Math.Max(0,
            x.Quantity - x.ConsumedQuantity - overrideReserved.GetValueOrDefault(x.Id)));
        var total = matrixRemaining + overrideRemaining;
        var remainingRequest = request.Quantity;

        if (matrixRequestAllowed && matrixRemaining > 0 && rule is not null && phase is not null)
        {
            var quantity = Math.Min(remainingRequest, matrixRemaining);
            allocations.Add(new("Matrix", phase.Id, quantity, periodStart, periodEnd));
            remainingRequest -= quantity;
        }
        foreach (var item in overrides.Where(_ => remainingRequest > 0))
        {
            var quantity = Math.Min(remainingRequest, Math.Max(0,
                item.Quantity - item.ConsumedQuantity - overrideReserved.GetValueOrDefault(item.Id)));
            if (quantity <= 0) continue;
            allocations.Add(new("ManualOverride", item.Id, quantity, item.ValidFrom, item.ValidTo));
            remainingRequest -= quantity;
        }

        var allowedResult = remainingRequest <= 0;
        var reasonCode = allowedResult ? "ALLOWED"
            : !matrixRequestAllowed && overrideRemaining <= 0 ? "BULK_ISSUE_NOT_ALLOWED"
            : rule is null && overrides.Count == 0 ? "RULE_NOT_FOUND"
            : "INSUFFICIENT_ENTITLEMENT";
        var message = allowedResult ? "KKD hakkı uygundur."
            : reasonCode == "BULK_ISSUE_NOT_ALLOWED" ? "Talep miktarı, kuralın tek seferde verilebilecek miktarını aşıyor."
            : reasonCode == "RULE_NOT_FOUND" ? "Personelin stok veya grup için geçerli KKD kuralı bulunmuyor."
            : $"Talep edilen {request.Quantity:0.######} miktar için kalan hak {total:0.######}.";
        return new(
            allowedResult,
            reasonCode,
            message,
            employee.Id, stock.Id, groupCode, rule?.MatrixId, rule?.Id, phase?.Id, phase?.PhaseType.ToString(),
            request.Quantity, matrixRemaining, overrideRemaining, total, nextEligible, allocations);
    }

    private static (KkdEntitlementPhase? Phase, DateOnly Start, DateOnly? End, DateOnly? NextEligible) ResolvePhaseWindow(
        DateOnly employmentStart, DateOnly date, IReadOnlyCollection<KkdEntitlementPhase> phases)
    {
        var ordered = phases.OrderBy(x => x.OffsetMonths).ThenBy(x => x.SortOrder).ToArray();
        var eligible = ordered.Where(x => employmentStart.AddMonths(x.OffsetMonths) <= date).ToArray();
        var phase = eligible.LastOrDefault();
        if (phase is null)
            return (null, date, null, ordered.FirstOrDefault() is { } first ? employmentStart.AddMonths(first.OffsetMonths) : null);

        var baseStart = employmentStart.AddMonths(phase.OffsetMonths);
        if (phase.PhaseType != KkdEntitlementPhaseType.Recurring)
        {
            var next = ordered.FirstOrDefault(x => x.OffsetMonths > phase.OffsetMonths);
            DateOnly? end = next is null ? null : employmentStart.AddMonths(next.OffsetMonths).AddDays(-1);
            return (phase, baseStart, end, end?.AddDays(1));
        }

        var interval = Math.Max(1, phase.PeriodInterval ?? 1);
        var periodType = phase.PeriodType ?? KkdPeriodType.Year;
        var start = baseStart;
        while (AddPeriod(start, periodType, interval) <= date) start = AddPeriod(start, periodType, interval);
        var nextStart = AddPeriod(start, periodType, interval);
        return (phase, start, nextStart.AddDays(-1), nextStart);
    }

    private static decimal CalculateCarry(DateOnly employmentStart, KkdEntitlementPhase phase, decimal maxCarry,
        DateOnly currentStart, IEnumerable<KkdEntitlementConsumption> consumptions)
    {
        var interval = Math.Max(1, phase.PeriodInterval ?? 1);
        var type = phase.PeriodType ?? KkdPeriodType.Year;
        var previousStart = SubtractPeriod(currentStart, type, interval);
        if (previousStart < employmentStart.AddMonths(phase.OffsetMonths)) return 0;
        var previousEnd = currentStart.AddDays(-1);
        var previousConsumed = NetConsumed(consumptions.Where(x =>
        {
            var used = DateOnly.FromDateTime(x.ConsumedAtUtc.UtcDateTime);
            return used >= previousStart && used <= previousEnd;
        }));
        return Math.Min(maxCarry, Math.Max(0, phase.Quantity - previousConsumed));
    }

    private static (DateOnly Start, DateOnly End) EmploymentYear(DateOnly employmentStart, DateOnly date)
    {
        var start = employmentStart;
        while (start.AddYears(1) <= date) start = start.AddYears(1);
        return (start, start.AddYears(1).AddDays(-1));
    }

    private static decimal NetConsumed(IEnumerable<KkdEntitlementConsumption> rows) =>
        rows.Sum(x => x.IsReversal ? -x.Quantity : x.Quantity);

    private async Task<decimal> ReservedAsync(
        KkdEntitlementSourceType sourceType,
        long sourceId,
        DateOnly periodStart,
        DateOnly? periodEnd,
        CancellationToken ct) =>
        await uow.Repository<KkdDistributionEntitlementAllocation>().Query()
            .Where(x => x.SourceType == sourceType && x.SourceId == sourceId
                && x.PeriodStart == periodStart && x.PeriodEnd == periodEnd
                && (x.DistributionLine.Distribution.Status == KkdDistributionStatus.Draft
                    || x.DistributionLine.Distribution.Status == KkdDistributionStatus.Validated
                    || x.DistributionLine.Distribution.Status == KkdDistributionStatus.OutboundCreated))
            .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;

    private async Task<decimal> ReservedBetweenAsync(long phaseId, DateOnly start, DateOnly end, CancellationToken ct)
        => await ReservedBetweenAsync([phaseId], start, end, ct);

    private async Task<decimal> ReservedBetweenAsync(IReadOnlyCollection<long> phaseIds, DateOnly start, DateOnly end, CancellationToken ct)
    {
        if (phaseIds.Count == 0) return 0;
        var from = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusive = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await ActiveMatrixReservations(phaseIds)
            .Where(x => x.CreatedDate >= from && x.CreatedDate < toExclusive)
            .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0;
    }

    private async Task<int> ReservedIssueCountAsync(IReadOnlyCollection<long> phaseIds, DateOnly start, DateOnly end, CancellationToken ct)
    {
        if (phaseIds.Count == 0) return 0;
        var from = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toExclusive = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await ActiveMatrixReservations(phaseIds)
            .Where(x => x.CreatedDate >= from && x.CreatedDate < toExclusive)
            .Select(x => x.DistributionLineId)
            .Distinct()
            .CountAsync(ct);
    }

    private IQueryable<KkdDistributionEntitlementAllocation> ActiveMatrixReservations(long phaseId) =>
        ActiveMatrixReservations([phaseId]);

    private IQueryable<KkdDistributionEntitlementAllocation> ActiveMatrixReservations(IReadOnlyCollection<long> phaseIds) =>
        uow.Repository<KkdDistributionEntitlementAllocation>().Query()
            .Where(x => x.SourceType == KkdEntitlementSourceType.Matrix && phaseIds.Contains(x.SourceId)
                && (x.DistributionLine.Distribution.Status == KkdDistributionStatus.Draft
                    || x.DistributionLine.Distribution.Status == KkdDistributionStatus.Validated
                    || x.DistributionLine.Distribution.Status == KkdDistributionStatus.OutboundCreated));

    private static DateOnly AddPeriod(DateOnly date, KkdPeriodType type, int interval) => type switch
    {
        KkdPeriodType.Day => date.AddDays(interval),
        KkdPeriodType.Month => date.AddMonths(interval),
        _ => date.AddYears(interval)
    };

    private static DateOnly SubtractPeriod(DateOnly date, KkdPeriodType type, int interval) => type switch
    {
        KkdPeriodType.Day => date.AddDays(-interval),
        KkdPeriodType.Month => date.AddMonths(-interval),
        _ => date.AddYears(-interval)
    };

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static KkdEntitlementCheckResult Denied(KkdEntitlementCheckRequest request, long employeeId, string group,
        string code, string message) => new(false, code, message, employeeId, request.StockId, group, null, null, null,
        null, request.Quantity, 0, 0, 0, null, []);
}
