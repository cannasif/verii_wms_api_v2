using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdEntitlementServiceTests
{
    [Fact]
    public async Task Stock_rule_wins_over_group_rule_and_after_months_phase_is_selected()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 1, 1));
        fixture.AddRule("01", null, 20, 30);
        fixture.AddRule("01", fixture.Stock.Id, 2, 5);
        await fixture.SaveAsync();

        var result = await fixture.Service.CheckAsync(new(fixture.Employee.Id, fixture.Stock.Id, 5, new DateOnly(2026, 5, 1)));

        Assert.True(result.IsAllowed);
        Assert.Equal("AfterMonths", result.PhaseType);
        Assert.Equal(5, result.MatrixRemainingQuantity);
        Assert.Equal(fixture.Stock.Id, fixture.Db.KkdEntitlementRules.Single(x => x.Id == result.RuleId).StockId);
    }

    [Fact]
    public async Task Non_bulk_frequency_rule_rejects_a_single_request_above_frequency_quantity()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 7, 1));
        fixture.AddRule("01", null, 10, 10, allowBulk: false, frequencyDays: 30, quantityPerFrequency: 2);
        await fixture.SaveAsync();

        var result = await fixture.Service.CheckAsync(new(fixture.Employee.Id, fixture.Stock.Id, 3, new DateOnly(2026, 8, 3)));

        Assert.False(result.IsAllowed);
        Assert.Equal("BULK_ISSUE_NOT_ALLOWED", result.ReasonCode);
        Assert.Equal(2, result.MatrixRemainingQuantity);
    }

    [Fact]
    public async Task Active_distribution_reservation_reduces_remaining_entitlement()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 1, 1));
        var phase = fixture.AddRule("01", null, 4, 4).Phases.Single(x => x.PhaseType == KkdEntitlementPhaseType.Initial);
        await fixture.SaveAsync();
        fixture.AddReservation(phase.Id, 3, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));
        await fixture.SaveAsync();

        var result = await fixture.Service.CheckAsync(new(fixture.Employee.Id, fixture.Stock.Id, 2, new DateOnly(2026, 2, 1)));

        Assert.False(result.IsAllowed);
        Assert.Equal(1, result.MatrixRemainingQuantity);
        Assert.Equal("INSUFFICIENT_ENTITLEMENT", result.ReasonCode);
    }

    [Fact]
    public async Task Multiple_manual_overrides_are_allocated_in_expiry_order()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 1, 1));
        fixture.Db.KkdEmployeeEntitlementOverrides.AddRange(
            fixture.Override(2, new DateOnly(2026, 8, 10)),
            fixture.Override(3, new DateOnly(2026, 9, 10)));
        await fixture.SaveAsync();

        var result = await fixture.Service.CheckAsync(new(fixture.Employee.Id, fixture.Stock.Id, 4, new DateOnly(2026, 8, 3)));

        Assert.True(result.IsAllowed);
        Assert.Equal(5, result.OverrideRemainingQuantity);
        Assert.Equal([2m, 2m], result.Allocations.Select(x => x.Quantity).ToArray());
        Assert.All(result.Allocations, x => Assert.Equal("ManualOverride", x.SourceType));
    }

    [Fact]
    public async Task Annual_quantity_counts_active_reservations_from_every_phase_of_the_rule()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 1, 1));
        var rule = fixture.AddRule("01", null, 5, 5, annualQuantity: 5);
        await fixture.SaveAsync();
        var initial = rule.Phases.Single(x => x.PhaseType == KkdEntitlementPhaseType.Initial);
        fixture.AddReservation(initial.Id, 4, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31),
            new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc));
        await fixture.SaveAsync();

        var result = await fixture.Service.CheckAsync(new(fixture.Employee.Id, fixture.Stock.Id, 2, new DateOnly(2026, 5, 1)));

        Assert.False(result.IsAllowed);
        Assert.Equal(1, result.MatrixRemainingQuantity);
        Assert.Equal("INSUFFICIENT_ENTITLEMENT", result.ReasonCode);
    }

    [Fact]
    public async Task Annual_issue_count_blocks_a_new_issue_after_limit_is_consumed()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 1, 1));
        var rule = fixture.AddRule("01", null, 5, 5, annualIssueCount: 1);
        await fixture.SaveAsync();
        var phase = rule.Phases.Single(x => x.PhaseType == KkdEntitlementPhaseType.Initial);
        fixture.AddConsumption(rule.Id, phase.Id, 1, new DateTimeOffset(2026, 2, 1, 8, 0, 0, TimeSpan.Zero));
        await fixture.SaveAsync();

        var result = await fixture.Service.CheckAsync(new(fixture.Employee.Id, fixture.Stock.Id, 1, new DateOnly(2026, 2, 2)));

        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.MatrixRemainingQuantity);
        Assert.Equal("INSUFFICIENT_ENTITLEMENT", result.ReasonCode);
    }

    [Fact]
    public async Task Recurring_monthly_rule_carries_only_the_configured_maximum()
    {
        await using var fixture = await Fixture.CreateAsync(new DateOnly(2026, 1, 1));
        fixture.AddRecurringRule("01", quantity: 4, maxCarry: 2);
        await fixture.SaveAsync();

        var result = await fixture.Service.CheckAsync(new(fixture.Employee.Id, fixture.Stock.Id, 6, new DateOnly(2026, 2, 15)));

        Assert.True(result.IsAllowed);
        Assert.Equal(6, result.MatrixRemainingQuantity);
        var allocation = Assert.Single(result.Allocations);
        Assert.Equal(new DateOnly(2026, 2, 1), allocation.PeriodStart);
        Assert.Equal(new DateOnly(2026, 2, 28), allocation.PeriodEnd);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(WmsDbContext db, KkdEmployee employee, Stock stock)
        {
            Db = db;
            Employee = employee;
            Stock = stock;
            Service = new KkdEntitlementService(new UnitOfWork(db, new HttpContextAccessor()));
        }

        public WmsDbContext Db { get; }
        public KkdEmployee Employee { get; }
        public Stock Stock { get; }
        public KkdEntitlementService Service { get; }

        public static async Task<Fixture> CreateAsync(DateOnly employmentStart)
        {
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new WmsDbContext(options);
            var department = new KkdDepartment { BranchCode = "0", Code = "URETIM", Name = "Üretim" };
            var role = new KkdRole { BranchCode = "0", Code = "OP", Name = "Operatör" };
            var stock = new Stock { BranchCode = "0", ErpStockCode = "KKD-01", StockName = "Baret", GroupCode = "01" };
            db.AddRange(department, role, stock);
            await db.SaveChangesAsync();
            var employee = new KkdEmployee
            {
                BranchCode = "0", CustomerId = 1, EmployeeCode = "P001", FirstName = "Test", LastName = "Personel",
                DepartmentId = department.Id, RoleId = role.Id, QrCode = "QR-P001", EmploymentStartDate = employmentStart
            };
            db.Add(employee);
            await db.SaveChangesAsync();
            return new Fixture(db, employee, stock);
        }

        public KkdEntitlementRule AddRule(string group, long? stockId, decimal initial, decimal afterMonths,
            bool allowBulk = true, int? frequencyDays = null, decimal? quantityPerFrequency = null,
            int? annualIssueCount = null, decimal? annualQuantity = null)
        {
            var matrix = new KkdEntitlementMatrix
            {
                BranchCode = "0", CustomerId = Employee.CustomerId, DepartmentId = Employee.DepartmentId,
                RoleId = Employee.RoleId, Code = $"M-{Guid.NewGuid():N}", Name = "Matris"
            };
            var rule = new KkdEntitlementRule
            {
                BranchCode = "0", Matrix = matrix, GroupCode = group, StockId = stockId,
                AllowBulkIssue = allowBulk, AnnualIssueCount = annualIssueCount,
                AnnualQuantity = annualQuantity, IsActive = true
            };
            rule.Phases.Add(new KkdEntitlementPhase
            {
                BranchCode = "0", Rule = rule, PhaseType = KkdEntitlementPhaseType.Initial, OffsetMonths = 0,
                Quantity = initial, AllowBulkIssue = allowBulk, FrequencyDays = frequencyDays,
                QuantityPerFrequency = quantityPerFrequency, IsActive = true
            });
            rule.Phases.Add(new KkdEntitlementPhase
            {
                BranchCode = "0", Rule = rule, PhaseType = KkdEntitlementPhaseType.AfterMonths, OffsetMonths = 3,
                Quantity = afterMonths, AllowBulkIssue = allowBulk, IsActive = true
            });
            matrix.Rules.Add(rule);
            Db.Add(matrix);
            return rule;
        }

        public KkdEntitlementRule AddRecurringRule(string group, decimal quantity, decimal maxCarry)
        {
            var matrix = new KkdEntitlementMatrix
            {
                BranchCode = "0", CustomerId = Employee.CustomerId, DepartmentId = Employee.DepartmentId,
                RoleId = Employee.RoleId, Code = $"M-{Guid.NewGuid():N}", Name = "Periyodik matris"
            };
            var rule = new KkdEntitlementRule
            {
                BranchCode = "0", Matrix = matrix, GroupCode = group, AllowBulkIssue = true,
                MaxCarryQuantity = maxCarry, IsActive = true
            };
            rule.Phases.Add(new KkdEntitlementPhase
            {
                BranchCode = "0", Rule = rule, PhaseType = KkdEntitlementPhaseType.Recurring,
                OffsetMonths = 0, Quantity = quantity, AllowBulkIssue = true,
                PeriodType = KkdPeriodType.Month, PeriodInterval = 1, IsActive = true
            });
            matrix.Rules.Add(rule);
            Db.Add(matrix);
            return rule;
        }

        public void AddReservation(long phaseId, decimal quantity, DateOnly periodStart, DateOnly? periodEnd,
            DateTime? createdDate = null)
        {
            var distribution = new KkdDistribution
            {
                BranchCode = "0", CorrelationId = Guid.NewGuid(), EmployeeId = Employee.Id,
                CustomerId = Employee.CustomerId, WarehouseId = 1, DocumentSeriesId = 1,
                DocumentNo = $"KKD-{Guid.NewGuid():N}", Status = KkdDistributionStatus.OutboundCreated
            };
            var line = new KkdDistributionLine
            {
                BranchCode = "0", Distribution = distribution, LineNo = 1, StockId = Stock.Id,
                StockCodeSnapshot = Stock.ErpStockCode, GroupCode = Stock.GroupCode!, Quantity = quantity,
                EntitledQuantity = quantity, ExcessQuantity = 0, SourceLocationId = 1
            };
            line.EntitlementAllocations.Add(new KkdDistributionEntitlementAllocation
            {
                BranchCode = "0", DistributionLine = line, SourceType = KkdEntitlementSourceType.Matrix,
                SourceId = phaseId, Quantity = quantity, PeriodStart = periodStart, PeriodEnd = periodEnd,
                CreatedDate = createdDate ?? DateTime.UtcNow
            });
            distribution.Lines.Add(line);
            Db.Add(distribution);
        }

        public void AddConsumption(long ruleId, long phaseId, decimal quantity, DateTimeOffset consumedAt)
        {
            var distribution = new KkdDistribution
            {
                BranchCode = "0", CorrelationId = Guid.NewGuid(), EmployeeId = Employee.Id,
                CustomerId = Employee.CustomerId, WarehouseId = 1, DocumentSeriesId = 1,
                DocumentNo = $"KKD-{Guid.NewGuid():N}", Status = KkdDistributionStatus.Completed
            };
            var line = new KkdDistributionLine
            {
                BranchCode = "0", Distribution = distribution, LineNo = 1, StockId = Stock.Id,
                StockCodeSnapshot = Stock.ErpStockCode, GroupCode = Stock.GroupCode!, Quantity = quantity,
                EntitledQuantity = quantity, ExcessQuantity = 0, SourceLocationId = 1
            };
            line.Consumptions.Add(new KkdEntitlementConsumption
            {
                BranchCode = "0", EmployeeId = Employee.Id,
                DistributionLine = line, StockId = Stock.Id, GroupCode = Stock.GroupCode!,
                SourceType = KkdEntitlementSourceType.Matrix, RuleId = ruleId, PhaseId = phaseId,
                Quantity = quantity, ConsumedAtUtc = consumedAt
            });
            distribution.Lines.Add(line);
            Db.Add(distribution);
        }

        public KkdEmployeeEntitlementOverride Override(decimal quantity, DateOnly validTo) => new()
        {
            BranchCode = "0", EmployeeId = Employee.Id, GroupCode = Stock.GroupCode!, Quantity = quantity,
            ValidFrom = new DateOnly(2026, 1, 1), ValidTo = validTo, Reason = "Test", ApprovedByUserId = 1
        };

        public Task SaveAsync() => Db.SaveChangesAsync();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
