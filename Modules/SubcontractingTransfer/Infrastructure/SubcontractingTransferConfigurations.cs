using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.SubcontractingTransfer.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.SubcontractingTransfer.Infrastructure;

public sealed class SubcontractingTransferHeaderLinkConfiguration : BaseEntityConfiguration<SubcontractingTransferHeaderLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SubcontractingTransferHeaderLink> b)
    {
        b.ToTable("RII_ST_HEADER_LINK");
        b.Property(x=>x.Direction).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.OwnershipType).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.SupplierCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x=>x.SupplierNameSnapshot).HasMaxLength(300).IsRequired();
        b.Property(x=>x.SubcontractOrderNo).HasMaxLength(100);
        b.Property(x=>x.OperationCode).HasMaxLength(100);
        b.Property(x=>x.SupplierDispatchNo).HasMaxLength(100);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.WarehouseTransferHeader).WithOne()
            .HasForeignKey<SubcontractingTransferHeaderLink>(x=>x.WarehouseTransferHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.Supplier).WithMany().HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.ParentIssueTransfer).WithMany().HasForeignKey(x=>x.ParentIssueTransferId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.WarehouseTransferHeaderId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.BranchCode,x.SupplierId,x.Direction,x.ExpectedReturnAtUtc});
        b.HasIndex(x=>new{x.BranchCode,x.SubcontractOrderNo,x.Direction});
    }
}

public sealed class SubcontractingTransferLineLinkConfiguration : BaseEntityConfiguration<SubcontractingTransferLineLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SubcontractingTransferLineLink> b)
    {
        b.ToTable("RII_ST_LINE_LINK",t=>t.HasCheckConstraint("CK_RII_ST_LINE_LINK_QTY","[ExpectedQuantity] > 0 AND [ScrapQuantity] >= 0 AND [ScrapQuantity] <= [ExpectedQuantity]"));
        b.Property(x=>x.LineRole).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.ExpectedQuantity).HasPrecision(20,6);
        b.Property(x=>x.ScrapQuantity).HasPrecision(20,6);
        b.Property(x=>x.RequirementReference).HasMaxLength(150);
        b.HasOne(x=>x.HeaderLink).WithMany(x=>x.Lines).HasForeignKey(x=>x.SubcontractingTransferHeaderLinkId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.WarehouseTransferLine).WithOne().HasForeignKey<SubcontractingTransferLineLink>(x=>x.WarehouseTransferLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.SourceIssueLine).WithMany().HasForeignKey(x=>x.SourceIssueLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.WarehouseTransferLineId).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class SubcontractingTransferPolicyConfiguration : BaseEntityConfiguration<SubcontractingTransferPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SubcontractingTransferPolicy> b)
    {
        b.ToTable("RII_ST_POLICIES",t=>{
            t.HasCheckConstraint("CK_RII_ST_POLICY_OVER_RECEIPT","[OverReceiptTolerancePercent] >= 0 AND [OverReceiptTolerancePercent] <= 100");
            t.HasCheckConstraint("CK_RII_ST_POLICY_LEAD_TIME","[DefaultLeadTimeDays] >= 0 AND [DefaultLeadTimeDays] <= 3650");
        });
        b.Property(x=>x.PolicyKey).HasMaxLength(30).IsRequired();
        b.Property(x=>x.OverReceiptTolerancePercent).HasPrecision(9,4);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>new{x.BranchCode,x.PolicyKey}).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
