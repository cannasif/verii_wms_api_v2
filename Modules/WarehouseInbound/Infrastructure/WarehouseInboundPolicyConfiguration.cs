using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Infrastructure;

public sealed class WarehouseInboundPolicyConfiguration : BaseEntityConfiguration<WarehouseInboundPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseInboundPolicy> b)
    {
        b.ToTable("RII_WI_POLICIES"); b.Property(x => x.PolicyKey).HasMaxLength(30).IsRequired(); b.Property(x => x.OverReceiptPolicy).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.OverReceiptTolerancePercent).HasPrecision(9, 4); b.Property(x => x.InventoryAvailabilityPolicy).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ErpPostingPolicy).HasConversion<string>().HasMaxLength(40); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.PolicyKey }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
