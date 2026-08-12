using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Infrastructure;

public sealed class GeneratorProductionProjectConfiguration : BaseEntityConfiguration<GeneratorProductionProject>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionProject> b)
    {
        b.ToTable("RII_GP_PROJECT", t => t.HasCheckConstraint("CK_RII_GP_PROJECT_VALUES", "[Quantity] > 0 AND [Priority] BETWEEN 0 AND 100 AND [PlannedDeliveryAtUtc] >= [PlannedStartAtUtc]"));
        b.Property(x => x.ProjectCode).HasMaxLength(100).IsRequired(); b.Property(x => x.ProjectName).HasMaxLength(300).IsRequired();
        b.Property(x => x.GeneratorType).HasMaxLength(100); b.Property(x => x.SerialNumber).HasMaxLength(100);
        b.Property(x => x.CustomerCodeSnapshot).HasMaxLength(100); b.Property(x => x.CustomerNameSnapshot).HasMaxLength(300);
        b.Property(x => x.ExternalWorkOrderNo).HasMaxLength(100); b.Property(x => x.SourceSystemCode).HasMaxLength(50);
        b.Property(x => x.Description).HasMaxLength(2000); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.ProductionHeader).WithMany().HasForeignKey(x => x.ProductionHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.ProjectCode }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BranchCode, x.Status, x.PlannedDeliveryAtUtc });
    }
}

public sealed class GeneratorProductionStationConfiguration : BaseEntityConfiguration<GeneratorProductionStation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionStation> b)
    {
        b.ToTable("RII_GP_STATION", t => t.HasCheckConstraint("CK_RII_GP_STATION_CAPACITY", "[MaxParallelJobs] > 0 AND [DefaultPersonnelCapacity] >= 0"));
        b.Property(x => x.Code).HasMaxLength(50).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Area).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Description).HasMaxLength(1000); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.BranchCode, x.Area, x.PlanningOrder });
    }
}

public sealed class GeneratorProductionShiftConfiguration : BaseEntityConfiguration<GeneratorProductionShift>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionShift> b)
    {
        b.ToTable("RII_GP_SHIFT"); b.Property(x => x.Code).HasMaxLength(30).IsRequired(); b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.BranchCode, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class GeneratorProductionStationShiftConfiguration : BaseEntityConfiguration<GeneratorProductionStationShift>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionStationShift> b)
    {
        b.ToTable("RII_GP_STATION_SHIFT", t => t.HasCheckConstraint("CK_RII_GP_STATION_SHIFT_CAPACITY", "[WeekdayMask] BETWEEN 0 AND 127 AND [CapacityMinutes] >= 0 AND [PersonnelCapacity] >= 0 AND [MachineCapacity] >= 0"));
        b.HasOne(x => x.Station).WithMany(x => x.Shifts).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StationId, x.ShiftId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class GeneratorProductionCalendarExceptionConfiguration : BaseEntityConfiguration<GeneratorProductionCalendarException>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionCalendarException> b)
    {
        b.ToTable("RII_GP_CALENDAR_EXCEPTION", t => t.HasCheckConstraint("CK_RII_GP_CALENDAR_EXCEPTION_CAPACITY", "[CapacityMinutes] IS NULL OR [CapacityMinutes] >= 0"));
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired(); b.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.BranchCode, x.ExceptionDate, x.StationId, x.ShiftId });
    }
}

public sealed class GeneratorProductionResourceConfiguration : BaseEntityConfiguration<GeneratorProductionResource>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionResource> b)
    {
        b.ToTable("RII_GP_RESOURCE", t => t.HasCheckConstraint("CK_RII_GP_RESOURCE_CAPACITY", "[Capacity] > 0")); b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.ResourceType).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => new { x.BranchCode, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class GeneratorProductionStationResourceConfiguration : BaseEntityConfiguration<GeneratorProductionStationResource>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionStationResource> b)
    {
        b.ToTable("RII_GP_STATION_RESOURCE", t => t.HasCheckConstraint("CK_RII_GP_STATION_RESOURCE_QTY", "[RequiredQuantity] > 0"));
        b.HasOne(x => x.Station).WithMany(x => x.Resources).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Resource).WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StationId, x.ResourceId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class GeneratorProductionRouteConfiguration : BaseEntityConfiguration<GeneratorProductionRoute>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionRoute> b)
    {
        b.ToTable("RII_GP_ROUTE", t => t.HasCheckConstraint("CK_RII_GP_ROUTE_VERSION", "[VersionNumber] > 0")); b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.PartType).HasConversion<string>().HasMaxLength(30); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.Code, x.VersionNumber }).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.BranchCode, x.PartType, x.IsActive });
    }
}

public sealed class GeneratorProductionRouteOperationConfiguration : BaseEntityConfiguration<GeneratorProductionRouteOperation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionRouteOperation> b)
    {
        b.ToTable("RII_GP_ROUTE_OPERATION", t => t.HasCheckConstraint("CK_RII_GP_ROUTE_OPERATION_DURATION", "[Sequence] > 0 AND [DurationMinutes] > 0 AND [MinimumDurationMinutes] > 0 AND [MaximumDurationMinutes] >= [MinimumDurationMinutes] AND [DurationMinutes] BETWEEN [MinimumDurationMinutes] AND [MaximumDurationMinutes]"));
        b.Property(x => x.OperationCode).HasMaxLength(50).IsRequired(); b.Property(x => x.OperationName).HasMaxLength(200).IsRequired(); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Route).WithMany(x => x.Operations).HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.RouteId, x.Sequence }).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.BranchCode, x.StationId, x.IsActive });
    }
}

public sealed class GeneratorProductionRouteDependencyConfiguration : BaseEntityConfiguration<GeneratorProductionRouteDependency>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionRouteDependency> b)
    {
        b.ToTable("RII_GP_ROUTE_DEPENDENCY"); b.Property(x => x.DependencyType).HasConversion<string>().HasMaxLength(30);
        b.HasOne(x => x.Route).WithMany(x => x.Dependencies).HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PredecessorOperation).WithMany().HasForeignKey(x => x.PredecessorOperationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SuccessorOperation).WithMany().HasForeignKey(x => x.SuccessorOperationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PredecessorOperationId, x.SuccessorOperationId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class GeneratorProductionOperationConfiguration : BaseEntityConfiguration<GeneratorProductionOperation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionOperation> b)
    {
        b.ToTable("RII_GP_OPERATION", t => t.HasCheckConstraint("CK_RII_GP_OPERATION_VALUES", "[UnitIndex] > 0 AND [PlannedEndAtUtc] > [PlannedStartAtUtc] AND [GoodQuantity] >= 0 AND [DefectQuantity] >= 0 AND [ScrapQuantity] >= 0"));
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30); b.Property(x => x.ProblemDescription).HasMaxLength(1000); b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.GoodQuantity).HasPrecision(20, 6); b.Property(x => x.DefectQuantity).HasPrecision(20, 6); b.Property(x => x.ScrapQuantity).HasPrecision(20, 6);
        b.HasOne(x => x.Project).WithMany(x => x.Operations).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RouteOperation).WithMany().HasForeignKey(x => x.RouteOperationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Station).WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ProductionOrder).WithMany().HasForeignKey(x => x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ProjectId, x.RouteOperationId, x.UnitIndex }).IsUnique().HasFilter("[IsDeleted] = 0"); b.HasIndex(x => new { x.BranchCode, x.StationId, x.PlannedStartAtUtc, x.PlannedEndAtUtc });
    }
}

public sealed class GeneratorProductionOperationDependencyConfiguration : BaseEntityConfiguration<GeneratorProductionOperationDependency>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionOperationDependency> b)
    {
        b.ToTable("RII_GP_OPERATION_DEPENDENCY"); b.Property(x => x.DependencyType).HasConversion<string>().HasMaxLength(30);
        b.HasOne(x => x.PredecessorOperation).WithMany(x => x.Successors).HasForeignKey(x => x.PredecessorOperationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SuccessorOperation).WithMany(x => x.Predecessors).HasForeignKey(x => x.SuccessorOperationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PredecessorOperationId, x.SuccessorOperationId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class GeneratorProductionPlanRevisionConfiguration : BaseEntityConfiguration<GeneratorProductionPlanRevision>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionPlanRevision> b)
    {
        b.ToTable("RII_GP_PLAN_REVISION"); b.Property(x => x.ActionType).HasMaxLength(50).IsRequired(); b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        b.Property(x => x.PreviousPlanJson).HasColumnType("nvarchar(max)"); b.Property(x => x.NewPlanJson).HasColumnType("nvarchar(max)").IsRequired();
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.BranchCode, x.ProjectId, x.OccurredAtUtc });
    }
}

public sealed class GeneratorProductionRuleConfiguration : BaseEntityConfiguration<GeneratorProductionRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratorProductionRule> b)
    {
        b.ToTable("RII_GP_RULE"); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired(); b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ParametersJson).HasColumnType("nvarchar(max)"); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
