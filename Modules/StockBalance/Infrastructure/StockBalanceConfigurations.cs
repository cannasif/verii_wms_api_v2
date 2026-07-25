using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.StockBalance.Infrastructure;

public sealed class LocationStockBalanceConfiguration : BaseEntityConfiguration<LocationStockBalance>
{
    protected override void ConfigureEntity(EntityTypeBuilder<LocationStockBalance> builder)
    {
        builder.ToTable("RII_LOCATION_STOCK_BALANCE");
        builder.Property(x => x.DimensionKey).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LotNo).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SerialNo).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StockStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.ReservedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.AvailableQuantity).HasPrecision(18, 6);
        builder.HasIndex(x => x.DimensionKey).IsUnique().HasDatabaseName("UX_RII_LOCATION_STOCK_BALANCE_DIMENSION_KEY");
        builder.HasIndex(x => new { x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, x.StockStatus })
            .HasDatabaseName("IX_RII_LOCATION_STOCK_BALANCE_DIMENSIONS");
        builder.HasIndex(x => new { x.StockId, x.WarehouseId, x.AvailableQuantity }).HasDatabaseName("IX_RII_LOCATION_STOCK_BALANCE_PICKING");
        builder.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Stock.Domain.Stock>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.YapCode.Domain.YapCode>().WithMany().HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WarehouseStockBalanceConfiguration : BaseEntityConfiguration<WarehouseStockBalance>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseStockBalance> builder)
    {
        builder.ToTable("RII_WAREHOUSE_STOCK_BALANCE");
        builder.Property(x => x.DimensionKey).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.StockStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.ReservedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.AvailableQuantity).HasPrecision(18, 6);
        builder.HasIndex(x => x.DimensionKey).IsUnique().HasDatabaseName("UX_RII_WAREHOUSE_STOCK_BALANCE_DIMENSION_KEY");
        builder.HasIndex(x => new { x.WarehouseId, x.StockId, x.YapCodeId, x.UnitCode, x.StockStatus })
            .HasDatabaseName("IX_RII_WAREHOUSE_STOCK_BALANCE_DIMENSIONS");
        builder.HasIndex(x => new { x.StockId, x.AvailableQuantity }).HasDatabaseName("IX_RII_WAREHOUSE_STOCK_BALANCE_STOCK");
        builder.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Stock.Domain.Stock>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.YapCode.Domain.YapCode>().WithMany().HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockBalanceProjectionStateConfiguration : BaseEntityConfiguration<StockBalanceProjectionState>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StockBalanceProjectionState> builder)
    {
        builder.ToTable("RII_STOCK_BALANCE_PROJECTION_STATE");
        builder.Property(x => x.ProjectionName).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.ProjectionName).IsUnique().HasDatabaseName("UX_RII_STOCK_BALANCE_PROJECTION_STATE_NAME");
    }
}

public sealed class StockReservationOperationConfiguration : BaseEntityConfiguration<StockReservationOperation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StockReservationOperation> builder)
    {
        builder.ToTable("RII_STOCK_RESERVATION_OPERATIONS");
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ReferenceNo).HasMaxLength(100);
        builder.Property(x => x.OperationType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_RII_STOCK_RESERVATION_OPERATIONS_IDEMPOTENCY");
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId }).HasDatabaseName("IX_RII_STOCK_RESERVATION_OPERATIONS_REFERENCE");
    }
}

public sealed class StockReservationEntryConfiguration : BaseEntityConfiguration<StockReservationEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StockReservationEntry> builder)
    {
        builder.ToTable("RII_STOCK_RESERVATION_ENTRIES");
        builder.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LotNo).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SerialNo).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StockStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.QuantityDelta).HasPrecision(18, 6);
        builder.HasOne(x => x.Operation).WithMany(x => x.Entries).HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ReferenceLineId, x.WarehouseId, x.LocationId, x.StockId })
            .HasDatabaseName("IX_RII_STOCK_RESERVATION_ENTRIES_REFERENCE_LINE");
        builder.HasIndex(x => new { x.WarehouseId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, x.StockStatus })
            .HasDatabaseName("IX_RII_STOCK_RESERVATION_ENTRIES_DIMENSIONS");
    }
}
