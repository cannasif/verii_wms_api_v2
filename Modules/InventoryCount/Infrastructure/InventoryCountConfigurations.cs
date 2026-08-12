using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.InventoryCount.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared.Infrastructure;
using DocumentSeriesEntity = verii_wms_api_v2.Modules.DocumentSeries.Domain.DocumentSeries;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.InventoryCount.Infrastructure;

public sealed class InventoryCountHeaderConfiguration : BaseEntityConfiguration<InventoryCountHeader>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountHeader> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_HEADER", t =>
        {
            t.HasCheckConstraint("CK_RII_IC_HEADER_PRIORITY", "[Priority] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_RII_IC_HEADER_TOLERANCE", "[QuantityTolerance] >= 0 AND [PercentageTolerance] >= 0");
            t.HasCheckConstraint("CK_RII_IC_HEADER_ATTEMPTS", "[MaxCountAttempts] BETWEEN 1 AND 10");
        });
        b.Property(x => x.DocumentNo).HasMaxLength(50).IsRequired();
        b.Property(x => x.CountType).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.CountMode).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.MovementPolicy).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.QuantityTolerance).HasPrecision(18, 6);
        b.Property(x => x.PercentageTolerance).HasPrecision(9, 4);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ReleaseIdempotencyKey).HasMaxLength(100).IsUnicode(false);
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DocumentSeriesEntity>().WithMany().HasForeignKey(x => x.DocumentSeriesId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CountCode).IsUnique().HasDatabaseName("UX_RII_IC_HEADER_COUNT_CODE");
        b.HasIndex(x => new { x.BranchCode, x.DocumentNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_IC_HEADER_BRANCH_DOCUMENT");
        b.HasIndex(x => new { x.WarehouseId, x.Status, x.PlannedStartUtc }).HasDatabaseName("IX_RII_IC_HEADER_WAREHOUSE_STATUS_PLAN");
        b.HasIndex(x => x.ReleaseIdempotencyKey).IsUnique().HasFilter("[ReleaseIdempotencyKey] IS NOT NULL").HasDatabaseName("UX_RII_IC_HEADER_RELEASE_IDEMPOTENCY");
    }
}

public sealed class InventoryCountScopeConfiguration : BaseEntityConfiguration<InventoryCountScope>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountScope> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_SCOPE");
        b.Property(x => x.StockGroupCode).HasMaxLength(50);
        b.HasOne(x => x.Header).WithMany(x => x.Scopes).HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockEntity>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<YapCodeEntity>().WithMany().HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HeaderId, x.SequenceNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_IC_SCOPE_HEADER_SEQUENCE");
    }
}

public sealed class InventoryCountTaskConfiguration : BaseEntityConfiguration<InventoryCountTask>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountTask> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_TASK");
        b.Property(x => x.TaskNo).HasMaxLength(60).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        b.HasOne(x => x.Header).WithMany(x => x.Tasks).HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCountTask>().WithMany().HasForeignKey(x => x.PreviousTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TaskCode).IsUnique().HasDatabaseName("UX_RII_IC_TASK_CODE");
        b.HasIndex(x => new { x.BranchCode, x.TaskNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_IC_TASK_BRANCH_NO");
        b.HasIndex(x => new { x.LocationId, x.Status }).HasDatabaseName("IX_RII_IC_TASK_LOCATION_STATUS");
        b.HasIndex(x => new { x.AssignedUserId, x.Status, x.RouteSequence }).HasDatabaseName("IX_RII_IC_TASK_ASSIGNEE_STATUS_ROUTE");
    }
}

public sealed class InventoryCountLineConfiguration : BaseEntityConfiguration<InventoryCountLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountLine> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_LINE");
        b.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.LotNo).HasMaxLength(100).IsRequired();
        b.Property(x => x.SerialNo).HasMaxLength(100).IsRequired();
        b.Property(x => x.StockStatus).HasMaxLength(30).IsRequired();
        b.Property(x => x.SnapshotQuantity).HasPrecision(18, 6);
        b.Property(x => x.CountedQuantity).HasPrecision(18, 6);
        b.Property(x => x.VarianceQuantity).HasPrecision(18, 6);
        b.Property(x => x.VariancePercentage).HasPrecision(9, 4);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.DifferenceReasonCode).HasMaxLength(50);
        b.Property(x => x.ReviewNote).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        b.HasOne(x => x.Header).WithMany(x => x.Lines).HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Task).WithMany(x => x.Lines).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockEntity>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<YapCodeEntity>().WithMany().HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TaskId, x.SequenceNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_IC_LINE_TASK_SEQUENCE");
        b.HasIndex(x => new { x.HeaderId, x.LocationId, x.StockId, x.YapCodeId, x.UnitCode, x.StockStatus }).HasDatabaseName("IX_RII_IC_LINE_DIMENSION");
    }
}

public sealed class InventoryCountEntryConfiguration : BaseEntityConfiguration<InventoryCountEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountEntry> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_ENTRY", t => t.HasCheckConstraint("CK_RII_IC_ENTRY_QUANTITY", "[Quantity] >= 0"));
        b.Property(x => x.EntryType).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 6);
        b.Property(x => x.Barcode).HasMaxLength(500);
        b.Property(x => x.DeviceCode).HasMaxLength(100);
        b.Property(x => x.SessionCode).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasOne(x => x.Line).WithMany(x => x.Entries).HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<InventoryCountHeader>().WithMany().HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<InventoryCountTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_RII_IC_ENTRY_IDEMPOTENCY");
        b.HasIndex(x => new { x.TaskId, x.CountRound, x.EnteredAtUtc }).HasDatabaseName("IX_RII_IC_ENTRY_TASK_ROUND_TIME");
    }
}

public sealed class InventoryCountScanEventConfiguration : BaseEntityConfiguration<InventoryCountScanEvent>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountScanEvent> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_SCAN_EVENT");
        b.Property(x => x.Barcode).HasMaxLength(500).IsRequired();
        b.Property(x => x.ResultCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.ResultDetail).HasMaxLength(1000);
        b.Property(x => x.DeviceCode).HasMaxLength(100);
        b.HasOne<InventoryCountHeader>().WithMany().HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCountTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCountLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_RII_IC_SCAN_EVENT_IDEMPOTENCY");
        b.HasIndex(x => new { x.TaskId, x.ScannedAtUtc }).HasDatabaseName("IX_RII_IC_SCAN_EVENT_TASK_TIME");
    }
}

public sealed class InventoryCountReviewConfiguration : BaseEntityConfiguration<InventoryCountReview>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountReview> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_REVIEW");
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.PreviousQuantity).HasPrecision(18, 6);
        b.Property(x => x.ApprovedQuantity).HasPrecision(18, 6);
        b.Property(x => x.ReasonCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasOne<InventoryCountHeader>().WithMany().HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCountTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCountLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.HeaderId, x.ReviewedAtUtc }).HasDatabaseName("IX_RII_IC_REVIEW_HEADER_TIME");
    }
}

public sealed class InventoryCountAdjustmentConfiguration : BaseEntityConfiguration<InventoryCountAdjustment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountAdjustment> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_ADJUSTMENT");
        b.Property(x => x.QuantityDelta).HasPrecision(18, 6);
        b.Property(x => x.Status).HasMaxLength(30).IsRequired();
        b.HasOne<InventoryCountHeader>().WithMany().HasForeignKey(x => x.HeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCountLine>().WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockMovementOperation>().WithMany().HasForeignKey(x => x.StockMovementOperationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.LineId, x.StockMovementOperationId }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_IC_ADJUSTMENT_LINE_OPERATION");
    }
}

public sealed class InventoryCountPolicyConfiguration : BaseEntityConfiguration<InventoryCountPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InventoryCountPolicy> b)
    {
        b.ToTable("RII_INVENTORY_COUNT_POLICY", t =>
        {
            t.HasCheckConstraint("CK_RII_IC_POLICY_TOLERANCE", "[QuantityTolerance] >= 0 AND [PercentageTolerance] >= 0");
            t.HasCheckConstraint("CK_RII_IC_POLICY_ATTEMPTS", "[MaxCountAttempts] BETWEEN 1 AND 10");
        });
        b.Property(x => x.DefaultCountMode).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.DefaultMovementPolicy).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(x => x.QuantityTolerance).HasPrecision(18, 6);
        b.Property(x => x.PercentageTolerance).HasPrecision(9, 4);
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BranchCode, x.WarehouseId }).IsUnique().HasFilter("[IsDeleted] = 0 AND [IsActive] = 1").HasDatabaseName("UX_RII_IC_POLICY_BRANCH_WAREHOUSE");
    }
}
