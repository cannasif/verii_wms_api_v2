using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.ErpBalanceSync.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.ErpBalanceSync.Infrastructure;

public sealed class ErpStockBalanceSyncRunConfiguration : BaseEntityConfiguration<ErpStockBalanceSyncRun>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ErpStockBalanceSyncRun> builder)
    {
        builder.ToTable("RII_ERP_STOCK_BALANCE_SYNC_RUN");
        builder.Property(x => x.Mode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TriggerSource).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.StockCode).HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.TriggerReference).HasMaxLength(150);
        builder.Property(x => x.ErrorType).HasMaxLength(500);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.HasIndex(x => x.RunKey).IsUnique().HasDatabaseName("UX_RII_ERP_BALANCE_SYNC_RUN_KEY");
        builder.HasIndex(x => new { x.Status, x.StartedAtUtc }).HasDatabaseName("IX_RII_ERP_BALANCE_SYNC_RUN_STATUS_STARTED");
    }
}

public sealed class ErpWarehouseStockBalanceConfiguration : BaseEntityConfiguration<ErpWarehouseStockBalance>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ErpWarehouseStockBalance> builder)
    {
        builder.ToTable("RII_ERP_WAREHOUSE_STOCK_BALANCE");
        builder.Property(x => x.StockCode).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(x => x.UnitCode).HasMaxLength(20);
        builder.Property(x => x.ErpQuantity).HasPrecision(38, 8);
        builder.Property(x => x.WmsQuantityAtSync).HasPrecision(38, 8);
        builder.Property(x => x.Difference).HasPrecision(38, 8);
        builder.Property(x => x.MappingStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.WarehouseCode, x.StockCode }).IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_ERP_WAREHOUSE_STOCK_BALANCE_SOURCE");
        builder.HasIndex(x => new { x.WarehouseId, x.StockId }).HasDatabaseName("IX_RII_ERP_BALANCE_WMS_DIMENSION");
        builder.HasIndex(x => new { x.IsMissingInErp, x.MappingStatus, x.Difference }).HasDatabaseName("IX_RII_ERP_BALANCE_RECONCILIATION");
        builder.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Stock.Domain.Stock>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ErpStockBalanceSyncRun>().WithMany().HasForeignKey(x => x.LastSyncRunId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ErpStockBalanceChangeLogConfiguration : BaseEntityConfiguration<ErpStockBalanceChangeLog>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ErpStockBalanceChangeLog> builder)
    {
        builder.ToTable("RII_ERP_STOCK_BALANCE_CHANGE_LOG");
        builder.Property(x => x.StockCode).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(x => x.PreviousErpQuantity).HasPrecision(38, 8);
        builder.Property(x => x.CurrentErpQuantity).HasPrecision(38, 8);
        builder.Property(x => x.PreviousWmsQuantity).HasPrecision(38, 8);
        builder.Property(x => x.CurrentWmsQuantity).HasPrecision(38, 8);
        builder.Property(x => x.Difference).HasPrecision(38, 8);
        builder.Property(x => x.ChangeType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ReasonCode).HasMaxLength(60).IsUnicode(false).IsRequired();
        builder.HasOne<ErpStockBalanceSyncRun>().WithMany().HasForeignKey(x => x.SyncRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.WarehouseCode, x.StockCode, x.ObservedAtUtc })
            .HasDatabaseName("IX_RII_ERP_BALANCE_CHANGE_SOURCE_DATE");
        builder.HasIndex(x => new { x.SyncRunId, x.ChangeType }).HasDatabaseName("IX_RII_ERP_BALANCE_CHANGE_RUN_TYPE");
    }
}
