using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Infrastructure;

public sealed class ProductionTransferHeaderLinkConfiguration : BaseEntityConfiguration<ProductionTransferHeaderLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionTransferHeaderLink> b)
    {
        b.ToTable("RII_PT_HEADER_LINK");
        b.Property(x=>x.Purpose).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.MaterialAvailabilityStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.ProductionPlanNo).HasMaxLength(100);
        b.Property(x=>x.ProductionOrderNo).HasMaxLength(100);
        b.Property(x=>x.ProductionOperationCode).HasMaxLength(100);
        b.Property(x=>x.SourceWorkCenterCode).HasMaxLength(100);
        b.Property(x=>x.TargetWorkCenterCode).HasMaxLength(100);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.WarehouseTransferHeader).WithOne()
            .HasForeignKey<ProductionTransferHeaderLink>(x=>x.WarehouseTransferHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.ProductionHeader).WithMany()
            .HasForeignKey(x=>x.ProductionHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.ProductionOrder).WithMany()
            .HasForeignKey(x=>x.ProductionOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.WarehouseTransferHeaderId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.BranchCode,x.ProductionOrderNo,x.Purpose});
    }
}

public sealed class ProductionTransferLineLinkConfiguration : BaseEntityConfiguration<ProductionTransferLineLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionTransferLineLink> b)
    {
        b.ToTable("RII_PT_LINE_LINK",t=>t.HasCheckConstraint("CK_RII_PT_LINE_LINK_REQUIRED_QTY","[RequiredQuantity] > 0"));
        b.Property(x=>x.LineRole).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.RequirementReference).HasMaxLength(150);
        b.Property(x=>x.RequiredQuantity).HasPrecision(20,6);
        b.HasOne(x=>x.HeaderLink).WithMany(x=>x.Lines).HasForeignKey(x=>x.ProductionTransferHeaderLinkId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.WarehouseTransferLine).WithOne().HasForeignKey<ProductionTransferLineLink>(x=>x.WarehouseTransferLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.ProductionConsumption).WithMany()
            .HasForeignKey(x=>x.ProductionConsumptionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.ProductionOutput).WithMany()
            .HasForeignKey(x=>x.ProductionOutputId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.WarehouseTransferLineId).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class ProductionTransferPolicyConfiguration : BaseEntityConfiguration<ProductionTransferPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProductionTransferPolicy> b)
    {
        b.ToTable("RII_PT_POLICIES",t=>t.HasCheckConstraint("CK_RII_PT_POLICY_OVER_ISSUE","[OverIssueTolerancePercent] >= 0 AND [OverIssueTolerancePercent] <= 100"));
        b.Property(x=>x.PolicyKey).HasMaxLength(30).IsRequired();
        b.Property(x=>x.ProductionOrderSource).HasConversion<string>().HasMaxLength(40)
            .HasDefaultValue(ProductionOrderSourceType.NetsisErpFunctions)
            .HasSentinel((ProductionOrderSourceType)0);
        b.Property(x=>x.WmsSourceSystemCode).HasMaxLength(50).IsRequired().HasDefaultValue("WINDBOX");
        b.Property(x=>x.OverIssueTolerancePercent).HasPrecision(9,4);
        b.Property(x=>x.CancellationReturnPolicy).HasConversion<string>().HasMaxLength(40)
            .HasDefaultValue(WarehouseTransferCancellationReturnPolicy.OriginalSourceLocation)
            .HasSentinel((WarehouseTransferCancellationReturnPolicy)0);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>new{x.BranchCode,x.PolicyKey}).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
