using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Shared.Infrastructure;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Location.Infrastructure;

public sealed class WarehouseLocationConfiguration : BaseEntityConfiguration<WarehouseLocation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseLocation> builder)
    {
        builder.ToTable("RII_LOCATION", table =>
        {
            table.HasCheckConstraint("CK_RII_LOCATION_CAPACITY_QUANTITY", "[CapacityQuantity] IS NULL OR [CapacityQuantity] >= 0");
            table.HasCheckConstraint("CK_RII_LOCATION_CAPACITY_WEIGHT", "[CapacityWeight] IS NULL OR [CapacityWeight] >= 0");
            table.HasCheckConstraint("CK_RII_LOCATION_CAPACITY_VOLUME", "[CapacityVolume] IS NULL OR [CapacityVolume] >= 0");
        });

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.LocationType).HasMaxLength(30).IsRequired().HasDefaultValue(LocationTypes.Cell);
        builder.Property(x => x.BarcodeEntryMode).HasMaxLength(20).IsRequired().HasDefaultValue(BarcodeEntryModes.Auto);
        builder.Property(x => x.Barcode).HasMaxLength(100);
        builder.Property(x => x.ZoneCode).HasMaxLength(50);
        builder.Property(x => x.CapacityQuantity).HasPrecision(18, 6);
        builder.Property(x => x.CapacityWeight).HasPrecision(18, 6);
        builder.Property(x => x.CapacityVolume).HasPrecision(18, 6);
        builder.Property(x => x.CapacityUnit).HasMaxLength(20);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.AllowMixedStock).HasDefaultValue(false);
        builder.Property(x => x.AllowMixedLot).HasDefaultValue(false);
        builder.Property(x => x.AllowMixedStatus).HasDefaultValue(false);
        builder.Property(x => x.AllowCycleCount).HasDefaultValue(true);
        builder.Property(x => x.IsPickable).HasDefaultValue(true);
        builder.Property(x => x.IsPutaway).HasDefaultValue(true);
        builder.Property(x => x.IsQuarantine).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.ParentLocationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.BranchCode, x.WarehouseId, x.Code }).IsUnique()
            .HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_LOCATION_BRANCH_WAREHOUSE_CODE");
        builder.HasIndex(x => x.Barcode).IsUnique()
            .HasFilter("[Barcode] IS NOT NULL AND [IsDeleted] = 0").HasDatabaseName("UX_RII_LOCATION_BARCODE");
        builder.HasIndex(x => new { x.WarehouseId, x.ParentLocationId }).HasDatabaseName("IX_RII_LOCATION_WAREHOUSE_PARENT");
        builder.HasIndex(x => new { x.WarehouseId, x.LocationType, x.IsActive }).HasDatabaseName("IX_RII_LOCATION_WAREHOUSE_TYPE_ACTIVE");
    }
}
