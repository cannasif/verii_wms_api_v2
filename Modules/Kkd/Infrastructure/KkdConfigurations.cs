using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Infrastructure;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using UserEntity = verii_wms_api_v2.Modules.Identity.Domain.User;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Kkd.Infrastructure;

public sealed class KkdPolicyConfiguration : BaseEntityConfiguration<KkdPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdPolicy> b)
    {
        b.ToTable("RII_KKD_POLICY");
        b.Property(x => x.PolicyKey).HasMaxLength(30).IsRequired();
        b.Property(x => x.EnableMaterialRequestOrderFlow).HasDefaultValue(true);
        b.Property(x => x.RequireManagerApprovalForExcess).HasDefaultValue(true);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.PolicyKey }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class KkdDepartmentConfiguration : BaseEntityConfiguration<KkdDepartment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdDepartment> b)
    {
        b.ToTable("RII_KKD_DEPARTMENT");
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class KkdRoleConfiguration : BaseEntityConfiguration<KkdRole>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdRole> b)
    {
        b.ToTable("RII_KKD_ROLE");
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Department).WithMany(x => x.Roles).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.DepartmentId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class KkdEmployeeConfiguration : BaseEntityConfiguration<KkdEmployee>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdEmployee> b)
    {
        b.ToTable("RII_KKD_EMPLOYEE");
        b.Property(x => x.EmployeeCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.QrCode).HasMaxLength(200).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Department).WithMany(x => x.Employees).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Role).WithMany(x => x.Employees).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.EmployeeCode }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BranchCode, x.QrCode }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class KkdEntitlementMatrixConfiguration : BaseEntityConfiguration<KkdEntitlementMatrix>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdEntitlementMatrix> b)
    {
        b.ToTable("RII_KKD_MATRIX", t => t.HasCheckConstraint("CK_RII_KKD_MATRIX_DATES", "[EffectiveTo] IS NULL OR [EffectiveFrom] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        b.Property(x => x.Code).HasMaxLength(80).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BranchCode, x.CustomerId, x.DepartmentId, x.RoleId, x.IsActive });
    }
}

public sealed class KkdEntitlementRuleConfiguration : BaseEntityConfiguration<KkdEntitlementRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdEntitlementRule> b)
    {
        b.ToTable("RII_KKD_RULE", t =>
        {
            t.HasCheckConstraint("CK_RII_KKD_RULE_ANNUAL_COUNT", "[AnnualIssueCount] IS NULL OR [AnnualIssueCount] > 0");
            t.HasCheckConstraint("CK_RII_KKD_RULE_QUANTITY", "([AnnualQuantity] IS NULL OR [AnnualQuantity] >= 0) AND ([MaxCarryQuantity] IS NULL OR [MaxCarryQuantity] >= 0)");
        });
        b.Property(x => x.GroupCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.GroupName).HasMaxLength(200);
        b.Property(x => x.StockCodeSnapshot).HasMaxLength(100);
        b.Property(x => x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x => x.StandardCode).HasMaxLength(80);
        b.Property(x => x.StandardName).HasMaxLength(200);
        b.Property(x => x.AnnualQuantity).HasPrecision(20, 6);
        b.Property(x => x.MaxCarryQuantity).HasPrecision(20, 6);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Matrix).WithMany(x => x.Rules).HasForeignKey(x => x.MatrixId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.MatrixId, x.StockId, x.GroupCode }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BranchCode, x.StockId, x.GroupCode, x.IsActive });
    }
}

public sealed class KkdEntitlementPhaseConfiguration : BaseEntityConfiguration<KkdEntitlementPhase>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdEntitlementPhase> b)
    {
        b.ToTable("RII_KKD_PHASE", t => t.HasCheckConstraint("CK_RII_KKD_PHASE_VALUES", "[Quantity] >= 0 AND [OffsetMonths] >= 0 AND ([FrequencyDays] IS NULL OR [FrequencyDays] > 0) AND ([PeriodInterval] IS NULL OR [PeriodInterval] > 0)"));
        b.Property(x => x.PhaseType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.PeriodType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Quantity).HasPrecision(20, 6);
        b.Property(x => x.QuantityPerFrequency).HasPrecision(20, 6);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasOne(x => x.Rule).WithMany(x => x.Phases).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RuleId, x.PhaseType, x.OffsetMonths }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class KkdEmployeeEntitlementOverrideConfiguration : BaseEntityConfiguration<KkdEmployeeEntitlementOverride>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdEmployeeEntitlementOverride> b)
    {
        b.ToTable("RII_KKD_OVERRIDE", t => t.HasCheckConstraint("CK_RII_KKD_OVERRIDE_QTY", "[Quantity] > 0 AND [ConsumedQuantity] >= 0 AND [ConsumedQuantity] <= [Quantity] AND ([ValidTo] IS NULL OR [ValidTo] >= [ValidFrom])"));
        b.Property(x => x.GroupCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(20, 6);
        b.Property(x => x.ConsumedQuantity).HasPrecision(20, 6);
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Employee).WithMany(x => x.Overrides).HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.EmployeeId, x.GroupCode, x.IsActive });
    }
}

public sealed class KkdEmployeeStockPreferenceConfiguration : BaseEntityConfiguration<KkdEmployeeStockPreference>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdEmployeeStockPreference> b)
    {
        b.ToTable("RII_KKD_EMPLOYEE_STOCK_PREFERENCE");
        b.Property(x => x.GroupCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.EmployeeId, x.GroupCode }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BranchCode, x.StockId });
    }
}

public sealed class KkdRequestConfiguration : BaseEntityConfiguration<KkdRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdRequest> b)
    {
        b.ToTable("RII_KKD_REQUEST");
        b.Property(x => x.RequestNo).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ExternalRequestNo).HasMaxLength(100);
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.CancellationReason).HasMaxLength(1000);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CustomerEntity>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CorrelationId).IsUnique();
        b.HasIndex(x => new { x.BranchCode, x.RequestNo }).IsUnique();
        b.HasIndex(x => new { x.BranchCode, x.Status, x.Priority, x.NeededAtUtc });
        b.HasIndex(x => new { x.BranchCode, x.EmployeeId, x.Status });
        b.HasIndex(x => new { x.BranchCode, x.WarehouseId, x.Status });
    }
}

public sealed class KkdRequestLineConfiguration : BaseEntityConfiguration<KkdRequestLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdRequestLine> b)
    {
        b.ToTable("RII_KKD_REQUEST_LINE", t => t.HasCheckConstraint(
            "CK_RII_KKD_REQUEST_LINE_QTY",
            "[RequestedQuantity] > 0 AND [AllocatedQuantity] >= 0 AND [DeliveredQuantity] >= 0 AND [CancelledQuantity] >= 0 AND [AllocatedQuantity] + [DeliveredQuantity] + [CancelledQuantity] <= [RequestedQuantity]"));
        b.Property(x => x.GroupCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.GroupName).HasMaxLength(200);
        b.Property(x => x.StockCodeSnapshot).HasMaxLength(100);
        b.Property(x => x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.RequestedQuantity).HasPrecision(20, 6);
        b.Property(x => x.AllocatedQuantity).HasPrecision(20, 6);
        b.Property(x => x.DeliveredQuantity).HasPrecision(20, 6);
        b.Property(x => x.CancelledQuantity).HasPrecision(20, 6);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ExternalOrderNo).HasMaxLength(100);
        b.Property(x => x.ExternalOrderLineId).HasMaxLength(100);
        b.Property(x => x.ResolutionReason).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Request).WithMany(x => x.Lines).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockEntity>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RequestId, x.LineNo }).IsUnique();
        b.HasIndex(x => new { x.BranchCode, x.Status, x.GroupCode, x.StockId });
        b.HasIndex(x => new { x.BranchCode, x.ExternalOrderNo, x.ExternalOrderLineId })
            .HasFilter("[ExternalOrderNo] IS NOT NULL AND [ExternalOrderLineId] IS NOT NULL");
    }
}

public sealed class KkdRequestLineResolutionConfiguration : BaseEntityConfiguration<KkdRequestLineResolution>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdRequestLineResolution> b)
    {
        b.ToTable("RII_KKD_REQUEST_LINE_RESOLUTION");
        b.Property(x => x.StockCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x => x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.HasOne(x => x.RequestLine).WithMany(x => x.Resolutions)
            .HasForeignKey(x => x.RequestLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockEntity>().WithMany().HasForeignKey(x => x.PreviousStockId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockEntity>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.BranchCode, x.RequestLineId, x.ResolvedAtUtc });
    }
}

public sealed class KkdDistributionConfiguration : BaseEntityConfiguration<KkdDistribution>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdDistribution> b)
    {
        b.ToTable("RII_KKD_DISTRIBUTION");
        b.Property(x => x.DocumentNo).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ExcessApprovalStatus).HasConversion<string>().HasMaxLength(30)
            .HasDefaultValue(KkdExcessApprovalStatus.NotRequired)
            .HasSentinel((KkdExcessApprovalStatus)0);
        b.Property(x => x.ExcessApprovalReason).HasMaxLength(1000);
        b.Property(x => x.FailureReason).HasMaxLength(2000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CorrelationId).IsUnique();
        b.HasIndex(x => new { x.BranchCode, x.DocumentNo }).IsUnique();
        b.HasIndex(x => x.WarehouseOutboundId).IsUnique().HasFilter("[WarehouseOutboundId] IS NOT NULL");
        b.HasOne(x => x.KkdRequest).WithMany().HasForeignKey(x => x.KkdRequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.KkdRequestId).HasFilter("[KkdRequestId] IS NOT NULL");
    }
}

public sealed class KkdDistributionLineConfiguration : BaseEntityConfiguration<KkdDistributionLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdDistributionLine> b)
    {
        b.ToTable("RII_KKD_DISTRIBUTION_LINE", t => t.HasCheckConstraint("CK_RII_KKD_DISTRIBUTION_LINE_QTY", "[Quantity] > 0 AND [EntitledQuantity] >= 0 AND [ExcessQuantity] >= 0 AND [EntitledQuantity] + [ExcessQuantity] = [Quantity]"));
        b.Property(x => x.StockCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x => x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x => x.GroupCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(20, 6);
        b.Property(x => x.EntitledQuantity).HasPrecision(20, 6);
        b.Property(x => x.ExcessQuantity).HasPrecision(20, 6);
        b.Property(x => x.LotNo).HasMaxLength(100);
        b.Property(x => x.SerialNo).HasMaxLength(200);
        b.Property(x => x.OpenOrderNo).HasMaxLength(100);
        b.Property(x => x.OpenOrderLineId).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Distribution).WithMany(x => x.Lines).HasForeignKey(x => x.DistributionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.KkdRequestLine).WithMany().HasForeignKey(x => x.KkdRequestLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.DistributionId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.KkdRequestLineId).HasFilter("[KkdRequestLineId] IS NOT NULL");
    }
}

public sealed class KkdEntitlementConsumptionConfiguration : BaseEntityConfiguration<KkdEntitlementConsumption>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdEntitlementConsumption> b)
    {
        b.ToTable("RII_KKD_CONSUMPTION", t => t.HasCheckConstraint("CK_RII_KKD_CONSUMPTION_QTY", "[Quantity] > 0"));
        b.Property(x => x.GroupCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Quantity).HasPrecision(20, 6);
        b.HasOne(x => x.DistributionLine).WithMany(x => x.Consumptions).HasForeignKey(x => x.DistributionLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.EmployeeId, x.GroupCode, x.ConsumedAtUtc });
        b.HasIndex(x => x.ReversesConsumptionId).IsUnique().HasFilter("[ReversesConsumptionId] IS NOT NULL");
    }
}

public sealed class KkdDistributionEntitlementAllocationConfiguration : BaseEntityConfiguration<KkdDistributionEntitlementAllocation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdDistributionEntitlementAllocation> b)
    {
        b.ToTable("RII_KKD_DISTRIBUTION_ALLOCATION", t =>
        {
            t.HasCheckConstraint("CK_RII_KKD_DISTRIBUTION_ALLOCATION_QTY", "[Quantity] > 0");
            t.HasCheckConstraint("CK_RII_KKD_DISTRIBUTION_ALLOCATION_DATES", "[PeriodEnd] IS NULL OR [PeriodEnd] >= [PeriodStart]");
        });
        b.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Quantity).HasPrecision(20, 6);
        b.HasOne(x => x.DistributionLine).WithMany(x => x.EntitlementAllocations)
            .HasForeignKey(x => x.DistributionLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.DistributionLineId, x.SourceType, x.SourceId, x.PeriodStart }).IsUnique();
        b.HasIndex(x => new { x.BranchCode, x.SourceType, x.SourceId, x.PeriodStart, x.PeriodEnd });
    }
}

public sealed class KkdPreparationTaskConfiguration : BaseEntityConfiguration<KkdPreparationTask>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdPreparationTask> b)
    {
        b.ToTable("RII_KKD_PREPARATION_TASK");
        b.Property(x => x.TaskNo).HasMaxLength(60).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ClosureReason).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PreviousTask).WithMany().HasForeignKey(x => x.PreviousTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Distribution).WithMany().HasForeignKey(x => x.DistributionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CorrelationId).IsUnique();
        b.HasIndex(x => new { x.BranchCode, x.TaskNo }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BranchCode, x.AssignedUserId, x.Status });
        b.HasIndex(x => new { x.RequestId, x.Status });
        b.HasIndex(x => x.DistributionId).HasFilter("[DistributionId] IS NOT NULL");
    }
}

public sealed class KkdPreparationTaskLineConfiguration : BaseEntityConfiguration<KkdPreparationTaskLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdPreparationTaskLine> b)
    {
        b.ToTable("RII_KKD_PREPARATION_TASK_LINE", t => t.HasCheckConstraint(
            "CK_RII_KKD_PREPARATION_TASK_LINE_QTY",
            "[Quantity] > 0 AND [PreparedQuantity] >= 0 AND [DeliveredQuantity] >= 0"));
        b.Property(x => x.Quantity).HasPrecision(20, 6);
        b.Property(x => x.PreparedQuantity).HasPrecision(20, 6);
        b.Property(x => x.DeliveredQuantity).HasPrecision(20, 6);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Task).WithMany(x => x.Lines).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RequestLine).WithMany().HasForeignKey(x => x.RequestLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TaskId, x.RequestLineId }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => x.RequestLineId);
    }
}

public sealed class KkdValidationLogConfiguration : BaseEntityConfiguration<KkdValidationLog>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KkdValidationLog> b)
    {
        b.ToTable("RII_KKD_VALIDATION_LOG");
        b.Property(x => x.GroupCode).HasMaxLength(80);
        b.Property(x => x.AttemptedQuantity).HasPrecision(20, 6);
        b.Property(x => x.ReasonCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.Message).HasMaxLength(2000);
        b.Property(x => x.DeviceInfo).HasMaxLength(1000);
        b.HasIndex(x => new { x.BranchCode, x.CorrelationId });
        b.HasIndex(x => new { x.BranchCode, x.EmployeeId, x.CreatedDate });
    }
}
