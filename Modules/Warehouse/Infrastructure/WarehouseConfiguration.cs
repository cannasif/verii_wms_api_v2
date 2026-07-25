using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Warehouse.Infrastructure;

public sealed class WarehouseConfiguration : BaseEntityConfiguration<Domain.Warehouse>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Domain.Warehouse> builder)
    {
        builder.ToTable("RII_WAREHOUSE");
        builder.Property(x => x.WarehouseCode).IsRequired();
        builder.Property(x => x.WarehouseName).HasMaxLength(250).IsRequired();
        builder.HasIndex(x => new { x.BranchCode, x.WarehouseCode })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Warehouse_BranchCode_WarehouseCode");
        builder.HasIndex(x => x.WarehouseName).HasDatabaseName("IX_Warehouse_WarehouseName");
    }
}
