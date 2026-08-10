using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Production.Infrastructure;

public sealed class ProductionSourceWorkOrderConfiguration : BaseEntityConfiguration<ProductionSourceWorkOrder>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionSourceWorkOrder> b)
    {
        b.ToTable("RII_PR_SOURCE_ORDER",t=>t.HasCheckConstraint("CK_RII_PR_SOURCE_ORDER_QTY_REV","[PlannedQuantity] > 0 AND [RevisionNumber] > 0"));
        b.Property(x=>x.SourceSystemCode).HasMaxLength(50).IsRequired();
        b.Property(x=>x.ExternalKey).HasMaxLength(150).IsRequired();
        b.Property(x=>x.WorkOrderNumber).HasMaxLength(100).IsRequired();
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.ProductCode).HasMaxLength(100).IsRequired();
        b.Property(x=>x.ProductName).HasMaxLength(300);
        b.Property(x=>x.ConfigurationCode).HasMaxLength(100);
        b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x=>x.ProjectCode).HasMaxLength(100);
        b.Property(x=>x.PayloadHash).HasMaxLength(128);
        b.Property(x=>x.PlannedQuantity).HasPrecision(20,6);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>new{x.BranchCode,x.SourceSystemCode,x.ExternalKey}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.BranchCode,x.SourceSystemCode,x.WorkOrderNumber,x.RevisionNumber}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.BranchCode,x.SourceSystemCode,x.Status,x.SourceUpdatedAtUtc});
    }
}

public sealed class ProductionSourceRecipeLineConfiguration : BaseEntityConfiguration<ProductionSourceRecipeLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionSourceRecipeLine> b)
    {
        b.ToTable("RII_PR_SOURCE_RECIPE",t=>t.HasCheckConstraint(
            "CK_RII_PR_SOURCE_RECIPE_QTY",
            "[LineNumber] > 0 AND [OperationNumber] >= 0 AND [RecipeQuantity] > 0 AND [TotalRequiredQuantity] > 0 AND [VariableWasteQuantity] >= 0 AND [FixedWasteQuantity] >= 0"));
        b.Property(x=>x.ComponentStockCode).HasMaxLength(100).IsRequired();
        b.Property(x=>x.ComponentStockName).HasMaxLength(300);
        b.Property(x=>x.ComponentConfigurationCode).HasMaxLength(100);
        b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x=>x.RecipeQuantity).HasPrecision(20,6);
        b.Property(x=>x.VariableWasteQuantity).HasPrecision(20,6);
        b.Property(x=>x.FixedWasteQuantity).HasPrecision(20,6);
        b.Property(x=>x.TotalRequiredQuantity).HasPrecision(20,6);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.WorkOrder).WithMany(x=>x.RecipeLines).HasForeignKey(x=>x.ProductionSourceWorkOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.ProductionSourceWorkOrderId,x.LineNumber}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.ComponentStockCode,x.ComponentConfigurationCode});
    }
}

public sealed class ProductionHeaderConfiguration : BaseEntityConfiguration<ProductionHeader>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionHeader> b)
    {
        b.ToTable("RII_PR_HEADER");
        b.Property(x=>x.DocumentNo).HasMaxLength(50).IsRequired();
        b.Property(x=>x.PlanType).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.ExecutionMode).HasConversion<string>().HasMaxLength(20);
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.CustomerCodeSnapshot).HasMaxLength(100);
        b.Property(x=>x.CustomerNameSnapshot).HasMaxLength(300);
        b.Property(x=>x.Description).HasMaxLength(2000);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>new{x.BranchCode,x.DocumentNo}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>x.CorrelationId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.BranchCode,x.Status,x.PlannedStartAtUtc});
    }
}

public sealed class ProductionOrderConfiguration : BaseEntityConfiguration<ProductionOrder>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionOrder> b)
    {
        b.ToTable("RII_PR_ORDER",t=>t.HasCheckConstraint("CK_RII_PR_ORDER_QTY","[PlannedQuantity] > 0 AND [CompletedQuantity] >= 0 AND [ScrapQuantity] >= 0"));
        b.Property(x=>x.OrderNo).HasMaxLength(70).IsRequired();
        b.Property(x=>x.ExternalOrderNo).HasMaxLength(100);
        b.Property(x=>x.ExternalSourceSystemCode).HasMaxLength(50);
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.BomReference).HasMaxLength(100);
        b.Property(x=>x.RoutingReference).HasMaxLength(100);
        b.Property(x=>x.WorkCenterCode).HasMaxLength(100);
        b.Property(x=>x.ProducedStockCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x=>x.ProducedStockNameSnapshot).HasMaxLength(300);
        b.Property(x=>x.ProducedYapCodeSnapshot).HasMaxLength(100);
        b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x=>x.Description).HasMaxLength(1000);
        b.Property(x=>x.PlannedQuantity).HasPrecision(20,6);
        b.Property(x=>x.CompletedQuantity).HasPrecision(20,6);
        b.Property(x=>x.ScrapQuantity).HasPrecision(20,6);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.Header).WithMany(x=>x.Orders).HasForeignKey(x=>x.ProductionHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.ProductionHeaderId,x.LineNo}).IsUnique();
        b.HasIndex(x=>new{x.BranchCode,x.OrderNo}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.BranchCode,x.ExternalSourceSystemCode,x.ExternalOrderNo})
            .HasFilter("[IsDeleted] = 0 AND [ExternalOrderNo] IS NOT NULL");
        b.HasIndex(x=>new{x.ProducedStockId,x.Status});
    }
}

public sealed class ProductionMaterialRequirementConfiguration : BaseEntityConfiguration<ProductionMaterialRequirement>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionMaterialRequirement> b)
    {
        b.ToTable("RII_PR_MATERIAL",t=>t.HasCheckConstraint("CK_RII_PR_MATERIAL_QTY","[RequiredQuantity] > 0 AND [IssuedQuantity] >= 0 AND [ConsumedQuantity] >= 0"));
        b.Property(x=>x.StockCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x=>x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x=>x.YapCodeSnapshot).HasMaxLength(100);
        b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x=>x.IssueMode).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.TrackingType).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.RequiredQuantity).HasPrecision(20,6);
        b.Property(x=>x.IssuedQuantity).HasPrecision(20,6);
        b.Property(x=>x.ConsumedQuantity).HasPrecision(20,6);
        b.HasOne(x=>x.Order).WithMany(x=>x.Materials).HasForeignKey(x=>x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.ProductionOrderId,x.LineNo}).IsUnique();
        b.HasIndex(x=>new{x.StockId,x.SourceWarehouseId});
    }
}

public sealed class ProductionOutputExpectationConfiguration : BaseEntityConfiguration<ProductionOutputExpectation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionOutputExpectation> b)
    {
        b.ToTable("RII_PR_OUTPUT",t=>t.HasCheckConstraint("CK_RII_PR_OUTPUT_QTY","[PlannedQuantity] > 0 AND [ProducedQuantity] >= 0 AND [ScrapQuantity] >= 0"));
        b.Property(x=>x.StockCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x=>x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x=>x.YapCodeSnapshot).HasMaxLength(100);
        b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x=>x.TrackingType).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.PlannedQuantity).HasPrecision(20,6);
        b.Property(x=>x.ProducedQuantity).HasPrecision(20,6);
        b.Property(x=>x.ScrapQuantity).HasPrecision(20,6);
        b.HasOne(x=>x.Order).WithMany(x=>x.Outputs).HasForeignKey(x=>x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.ProductionOrderId,x.LineNo}).IsUnique();
        b.HasIndex(x=>new{x.StockId,x.TargetWarehouseId});
    }
}

public sealed class ProductionOrderAssignmentConfiguration : BaseEntityConfiguration<ProductionOrderAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionOrderAssignment> b)
    {
        b.ToTable("RII_PR_ASSIGNMENT");
        b.Property(x=>x.Note).HasMaxLength(500);
        b.HasOne(x=>x.Order).WithMany(x=>x.Assignments).HasForeignKey(x=>x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.ProductionOrderId,x.UserId}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.UserId,x.AcceptedAtUtc,x.CompletedAtUtc});
    }
}

public sealed class ProductionOrderDependencyConfiguration : BaseEntityConfiguration<ProductionOrderDependency>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionOrderDependency> b)
    {
        b.ToTable("RII_PR_DEPENDENCY",t=>t.HasCheckConstraint("CK_RII_PR_DEPENDENCY_SELF","[PredecessorOrderId] <> [SuccessorOrderId]"));
        b.Property(x=>x.DependencyType).HasConversion<string>().HasMaxLength(30);
        b.HasOne(x=>x.Header).WithMany(x=>x.Dependencies).HasForeignKey(x=>x.ProductionHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.PredecessorOrder).WithMany().HasForeignKey(x=>x.PredecessorOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.SuccessorOrder).WithMany().HasForeignKey(x=>x.SuccessorOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.PredecessorOrderId,x.SuccessorOrderId}).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class ProductionWorkOrderAssignmentCancellationConfiguration : BaseEntityConfiguration<ProductionWorkOrderAssignmentCancellation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionWorkOrderAssignmentCancellation> b)
    {
        b.ToTable("RII_PR_WO_ASSIGN_CANCEL");
        b.Property(x => x.WorkOrderNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.SourceSystemCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => new { x.BranchCode, x.WorkOrderNumber, x.Status })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 'Active'");
        b.HasIndex(x => new { x.BranchCode, x.CancelledAtUtc });
    }
}

public sealed class ProductionWorkOrderAssignmentCancellationLineConfiguration : BaseEntityConfiguration<ProductionWorkOrderAssignmentCancellationLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionWorkOrderAssignmentCancellationLine> b)
    {
        b.ToTable("RII_PR_WO_ASSIGN_CANCEL_LINE", t => t.HasCheckConstraint(
            "CK_RII_PR_WO_ASSIGN_CANCEL_LINE_QTY",
            "[CancelledQuantity] > 0 AND [OperationNumber] >= 0"));
        b.Property(x => x.CancelledQuantity).HasPrecision(20, 6);
        b.HasOne(x => x.Cancellation).WithMany(x => x.Lines).HasForeignKey(x => x.CancellationId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.CancellationId, x.StockId, x.YapCodeId, x.OperationNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
