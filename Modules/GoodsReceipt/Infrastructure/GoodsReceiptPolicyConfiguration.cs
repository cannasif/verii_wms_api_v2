using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;

public sealed class GoodsReceiptPolicyConfiguration : BaseEntityConfiguration<GoodsReceiptPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptPolicy> b)
    {
        b.ToTable("RII_GR_POLICIES"); b.Property(x => x.PolicyKey).HasMaxLength(30).IsRequired(); b.Property(x => x.OverReceiptPolicy).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.OverReceiptTolerancePercent).HasPrecision(9, 4); b.Property(x => x.InventoryAvailabilityPolicy).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ErpPostingPolicy).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ErpQualityGatePolicy).HasConversion<string>().HasMaxLength(40)
            .HasDefaultValue(GoodsReceiptErpQualityGatePolicy.AnyQualityPlan)
            .HasSentinel((GoodsReceiptErpQualityGatePolicy)0);
        b.Property(x => x.LocationSelectionPolicy)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(GoodsReceiptLocationSelectionPolicy.ReceivingOrStagingOnly);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.PolicyKey }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
