using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.StockMovement.Infrastructure;

public sealed class StockMovementOperationConfiguration : BaseEntityConfiguration<StockMovementOperation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StockMovementOperation> builder)
    {
        builder.ToTable("RII_STOCK_MOVEMENT_OPERATION");
        builder.Property(x => x.OperationCode).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OperationType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(50);
        builder.Property(x => x.ReferenceNo).HasMaxLength(100);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.OperationCode).IsUnique().HasDatabaseName("UX_RII_STOCK_MOVEMENT_OPERATION_CODE");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_RII_STOCK_MOVEMENT_OPERATION_IDEMPOTENCY");
        builder.HasIndex(x => x.ReversalOfOperationId).IsUnique().HasFilter("[ReversalOfOperationId] IS NOT NULL").HasDatabaseName("UX_RII_STOCK_MOVEMENT_OPERATION_REVERSAL");
        builder.HasIndex(x => new { x.OccurredAt, x.OperationType }).HasDatabaseName("IX_RII_STOCK_MOVEMENT_OPERATION_OCCURRED_TYPE");
        builder.HasOne<StockMovementOperation>().WithOne().HasForeignKey<StockMovementOperation>(x => x.ReversalOfOperationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockMovementEntryConfiguration : BaseEntityConfiguration<StockMovementEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StockMovementEntry> builder)
    {
        builder.ToTable("RII_STOCK_MOVEMENT");
        builder.Property(x => x.QuantityDelta).HasPrecision(18, 6).IsRequired();
        builder.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LotNo).HasMaxLength(100);
        builder.Property(x => x.SerialNo).HasMaxLength(100);
        builder.Property(x => x.StockStatus).HasMaxLength(30).IsRequired();
        builder.HasIndex(x => new { x.OperationId, x.LineNo }).IsUnique().HasDatabaseName("UX_RII_STOCK_MOVEMENT_OPERATION_LINE");
        builder.HasIndex(x => new { x.StockId, x.YapCodeId, x.WarehouseId, x.LocationId, x.UnitCode, x.OccurredAt }).HasDatabaseName("IX_RII_STOCK_MOVEMENT_BALANCE_STREAM");
        builder.HasIndex(x => new { x.WarehouseId, x.LocationId, x.OccurredAt }).HasDatabaseName("IX_RII_STOCK_MOVEMENT_LOCATION_TIME");
        builder.HasIndex(x => new { x.StockId, x.LotNo, x.SerialNo }).HasDatabaseName("IX_RII_STOCK_MOVEMENT_TRACE");
        builder.HasOne<StockMovementOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Stock.Domain.Stock>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.YapCode.Domain.YapCode>().WithMany().HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
