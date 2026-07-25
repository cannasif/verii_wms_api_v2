using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;

public sealed class GoodsReceiptTaskConfiguration : BaseEntityConfiguration<GoodsReceiptTask>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptTask> builder)
    {
        builder.ToTable("RII_GR_TASK", table => table.HasCheckConstraint("CK_RII_GR_TASK_PRIORITY", "[Priority] BETWEEN 1 AND 5"));
        builder.Property(x => x.TaskNo).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TaskType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Priority).HasDefaultValue((byte)3);
        builder.Property(x => x.ZoneCode).HasMaxLength(50);
        Utc(builder.Property(x => x.PlannedStartAtUtc));
        Utc(builder.Property(x => x.DueAtUtc));
        Utc(builder.Property(x => x.ReleasedAtUtc));
        Utc(builder.Property(x => x.StartedAtUtc));
        Utc(builder.Property(x => x.CompletedAtUtc));
        Utc(builder.Property(x => x.CancelledAtUtc));
        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.Header).WithMany(x => x.Tasks).HasForeignKey(x => x.GrHeaderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BranchCode, x.TaskNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_GR_TASK_BRANCH_TASK_NO");
        builder.HasIndex(x => new { x.GrHeaderId, x.TaskType, x.Status }).HasDatabaseName("IX_RII_GR_TASK_HEADER_TYPE_STATUS");
        builder.HasIndex(x => new { x.WarehouseId, x.Status, x.Priority, x.DueAtUtc }).HasDatabaseName("IX_RII_GR_TASK_WORK_QUEUE");
    }

    private static void Utc(PropertyBuilder<DateTimeOffset?> property) => property.HasColumnType("datetimeoffset(7)");
}

public sealed class GoodsReceiptTaskLineConfiguration : BaseEntityConfiguration<GoodsReceiptTaskLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptTaskLine> builder)
    {
        builder.ToTable("RII_GR_TASK_LINE", table =>
        {
            table.HasCheckConstraint("CK_RII_GR_TASK_LINE_SEQUENCE", "[SequenceNo] > 0");
            table.HasCheckConstraint("CK_RII_GR_TASK_LINE_QUANTITY", "[PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0");
        });
        builder.Property(x => x.PlannedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.ProcessedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.Task).WithMany(x => x.Lines).HasForeignKey(x => x.GrTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.GrLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.GrTaskId, x.SequenceNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_GR_TASK_LINE_TASK_SEQUENCE");
        builder.HasIndex(x => new { x.GrLineId, x.Status }).HasDatabaseName("IX_RII_GR_TASK_LINE_GR_LINE_STATUS");
    }
}

public sealed class GoodsReceiptTaskAssignmentConfiguration : BaseEntityConfiguration<GoodsReceiptTaskAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptTaskAssignment> builder)
    {
        builder.ToTable("RII_GR_TASK_ASSIGNMENT");
        builder.Property(x => x.AssignmentRole).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.AssignedAtUtc).HasColumnType("datetimeoffset(7)").IsRequired();
        Utc(builder.Property(x => x.AcceptedAtUtc));
        Utc(builder.Property(x => x.StartedAtUtc));
        Utc(builder.Property(x => x.CompletedAtUtc));
        Utc(builder.Property(x => x.UnassignedAtUtc));
        builder.Property(x => x.UnassignedReason).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.Task).WithMany(x => x.Assignments).HasForeignKey(x => x.GrTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Identity.Domain.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.GrTaskId, x.UserId }).IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] <> N'Unassigned' AND [Status] <> N'Rejected'")
            .HasDatabaseName("UX_RII_GR_TASK_ASSIGNMENT_ACTIVE_USER");
        builder.HasIndex(x => new { x.UserId, x.Status, x.AssignedAtUtc }).HasDatabaseName("IX_RII_GR_TASK_ASSIGNMENT_USER_QUEUE");
    }

    private static void Utc(PropertyBuilder<DateTimeOffset?> property) => property.HasColumnType("datetimeoffset(7)");
}

public sealed class GoodsReceiptTaskLineTrackingConfiguration : BaseEntityConfiguration<GoodsReceiptTaskLineTracking>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptTaskLineTracking> builder)
    {
        builder.ToTable("RII_GR_TASK_LINE_TRACKING", table =>
        {
            table.HasCheckConstraint("CK_RII_GR_TASK_LINE_TRACKING_SEQUENCE", "[SequenceNo] > 0");
            table.HasCheckConstraint("CK_RII_GR_TASK_LINE_TRACKING_QTY", "[PlannedQuantity] > 0");
        });
        builder.Property(x => x.PlannedQuantity).HasPrecision(18, 6).IsRequired();
        builder.Property(x => x.LotNo).HasMaxLength(100);
        builder.Property(x => x.SerialNo).HasMaxLength(100);
        builder.Property(x => x.ManufacturingDate).HasColumnType("date");
        builder.Property(x => x.ExpirationDate).HasColumnType("date");
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.TaskLine).WithMany(x => x.Trackings).HasForeignKey(x => x.GrTaskLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Stock.Domain.Stock>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x => x.TargetWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.GrTaskLineId, x.SequenceNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_GR_TASK_LINE_TRACKING_SEQUENCE");
        builder.HasIndex(x => new { x.GrTaskLineId, x.SerialNo }).IsUnique().HasFilter("[IsDeleted] = 0 AND [SerialNo] IS NOT NULL").HasDatabaseName("UX_RII_GR_TASK_LINE_TRACKING_SERIAL");
        builder.HasIndex(x => new { x.StockId, x.SerialNo }).IsUnique().HasFilter("[IsDeleted] = 0 AND [SerialNo] IS NOT NULL").HasDatabaseName("UX_RII_GR_TASK_LINE_TRACKING_STOCK_SERIAL");
    }
}

public sealed class GoodsReceiptLabelBatchConfiguration : BaseEntityConfiguration<GoodsReceiptLabelBatch>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptLabelBatch> builder)
    {
        builder.ToTable("RII_GR_LABEL_BATCH", table => table.HasCheckConstraint(
            "CK_RII_GR_LABEL_BATCH_COUNTS",
            "[TotalLabelCount] >= 0 AND [PrintedLabelCount] >= 0 AND [ConsumedLabelCount] >= 0 AND [VoidLabelCount] >= 0 AND [PrintedLabelCount] <= [TotalLabelCount] AND [ConsumedLabelCount] + [VoidLabelCount] <= [TotalLabelCount]"));
        builder.Property(x => x.BatchNo).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.LastPrintedAtUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.CompletedAtUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.Header).WithMany(x => x.LabelBatches).HasForeignKey(x => x.GrHeaderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CorrelationId).IsUnique().HasDatabaseName("UX_RII_GR_LABEL_BATCH_CORRELATION");
        builder.HasIndex(x => new { x.BranchCode, x.BatchNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_GR_LABEL_BATCH_BRANCH_BATCH_NO");
        builder.HasIndex(x => new { x.GrHeaderId, x.Status }).HasDatabaseName("IX_RII_GR_LABEL_BATCH_HEADER_STATUS");
    }
}

public sealed class GoodsReceiptLabelConfiguration : BaseEntityConfiguration<GoodsReceiptLabel>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptLabel> builder)
    {
        builder.ToTable("RII_GR_LABEL", table =>
        {
            table.HasCheckConstraint("CK_RII_GR_LABEL_QUANTITY", "[LabelQuantity] > 0");
            table.HasCheckConstraint("CK_RII_GR_LABEL_PRINT_COUNT", "[PrintCount] >= 0");
        });
        builder.Property(x => x.StockCodeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(x => x.StockNameSnapshot).HasMaxLength(250);
        builder.Property(x => x.YapCodeSnapshot).HasMaxLength(50);
        builder.Property(x => x.LabelQuantity).HasPrecision(18, 6);
        builder.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LotNo).HasMaxLength(100);
        builder.Property(x => x.SerialNo).HasMaxLength(100);
        builder.Property(x => x.ManufacturingDate).HasColumnType("date");
        builder.Property(x => x.ExpirationDate).HasColumnType("date");
        builder.Property(x => x.BarcodeValue).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        Utc(builder.Property(x => x.LastPrintedAtUtc));
        Utc(builder.Property(x => x.AssignedAtUtc));
        Utc(builder.Property(x => x.ConsumedAtUtc));
        builder.Property(x => x.VoidReason).HasMaxLength(500);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.Batch).WithMany(x => x.Labels).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptHeader>().WithMany().HasForeignKey(x => x.GrHeaderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptLine>().WithMany().HasForeignKey(x => x.GrLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptTaskLine>().WithMany().HasForeignKey(x => x.GrTaskLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Stock.Domain.Stock>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.YapCode.Domain.YapCode>().WithMany().HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.BarcodeValue).IsUnique().HasDatabaseName("UX_RII_GR_LABEL_BARCODE");
        builder.HasIndex(x => new { x.BatchId, x.Status }).HasDatabaseName("IX_RII_GR_LABEL_BATCH_STATUS");
        builder.HasIndex(x => new { x.GrHeaderId, x.GrLineId }).HasDatabaseName("IX_RII_GR_LABEL_HEADER_LINE");
        builder.HasIndex(x => new { x.StockId, x.LotNo, x.SerialNo }).HasDatabaseName("IX_RII_GR_LABEL_TRACE");
    }

    private static void Utc(PropertyBuilder<DateTimeOffset?> property) => property.HasColumnType("datetimeoffset(7)");
}
