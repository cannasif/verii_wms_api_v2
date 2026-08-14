using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Warehouse.Infrastructure;

public sealed class WarehouseConfiguration : BaseEntityConfiguration<Domain.Warehouse>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Domain.Warehouse> builder)
    {
        builder.ToTable("RII_WAREHOUSE");
        builder.Property(x => x.WarehouseCode).IsRequired();
        builder.Property(x => x.WarehouseName).HasMaxLength(250).IsRequired();
        builder.Property(x => x.AutoPickWithoutConfirmMaxQuantity).HasPrecision(18, 4);
        builder.HasOne<WarehouseLocation>()
            .WithMany()
            .HasForeignKey(x => x.DefaultGoodsReceiptLocationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.DefaultGoodsReceiptLocationId)
            .HasDatabaseName("IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION");
        builder.HasOne<WarehouseLocation>()
            .WithMany()
            .HasForeignKey(x => x.DefaultTransferReturnLocationId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.DefaultTransferReturnLocationId)
            .HasDatabaseName("IX_RII_WAREHOUSE_DEFAULT_TRANSFER_RETURN_LOCATION");
        builder.HasOne<WarehouseLocation>()
            .WithMany()
            .HasForeignKey(x => x.DefaultProductionTransferLocationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.DefaultProductionTransferLocationId)
            .HasDatabaseName("IX_RII_WAREHOUSE_DEFAULT_PRODUCTION_TRANSFER_LOCATION");
        builder.HasOne<WarehouseLocation>()
            .WithMany()
            .HasForeignKey(x => x.DefaultProductionTransferReturnLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.DefaultProductionTransferReturnLocationId)
            .HasDatabaseName("IX_RII_WAREHOUSE_DEFAULT_PRODUCTION_TRANSFER_RETURN_LOCATION");
        builder.HasOne<WarehouseLocation>()
            .WithMany()
            .HasForeignKey(x => x.ProductionPickingStagingLocationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.ProductionPickingStagingLocationId)
            .HasDatabaseName("IX_RII_WAREHOUSE_PRODUCTION_PICKING_STAGING_LOCATION");
        builder.HasOne<WarehouseLocation>()
            .WithMany()
            .HasForeignKey(x => x.KkdPickingStagingLocationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.KkdPickingStagingLocationId)
            .HasDatabaseName("IX_RII_WAREHOUSE_KKD_PICKING_STAGING_LOCATION");
        builder.HasIndex(x => new { x.BranchCode, x.WarehouseCode })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Warehouse_BranchCode_WarehouseCode");
        builder.HasIndex(x => x.WarehouseName).HasDatabaseName("IX_Warehouse_WarehouseName");
    }
}
