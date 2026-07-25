using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;

public sealed class GoodsReceiptExecutionConfiguration : BaseEntityConfiguration<GoodsReceiptExecution>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptExecution> b)
    {
        b.ToTable("RII_GR_EXECUTION");
        b.Property(x => x.ExecutionNo).HasMaxLength(60).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.OccurredAtUtc).HasColumnType("datetimeoffset(7)").IsRequired();
        b.Property(x => x.DeviceId).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Header).WithMany().HasForeignKey(x => x.GrHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.GrTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Modules.StockMovement.Domain.StockMovementOperation>().WithMany().HasForeignKey(x => x.StockMovementOperationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<GoodsReceiptExecution>().WithMany().HasForeignKey(x => x.ReversalOfExecutionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("UX_RII_GR_EXECUTION_IDEMPOTENCY");
        b.HasIndex(x => new { x.BranchCode, x.ExecutionNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_GR_EXECUTION_BRANCH_NO");
        b.HasIndex(x => new { x.GrHeaderId, x.OccurredAtUtc }).HasDatabaseName("IX_RII_GR_EXECUTION_HEADER_TIME");
    }
}

public sealed class GoodsReceiptExecutionLineConfiguration : BaseEntityConfiguration<GoodsReceiptExecutionLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptExecutionLine> b)
    {
        b.ToTable("RII_GR_EXECUTION_LINE", table =>
        {
            table.HasCheckConstraint("CK_RII_GR_EXECUTION_LINE_NO", "[LineNo] > 0");
            table.HasCheckConstraint("CK_RII_GR_EXECUTION_LINE_QTY", "[Quantity] > 0");
        });
        b.Property(x => x.Quantity).HasPrecision(18, 6).IsRequired();
        b.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.LotNo).HasMaxLength(100);
        b.Property(x => x.SerialNo).HasMaxLength(100);
        b.Property(x => x.SerialNumberRuleCodeSnapshot).HasMaxLength(50);
        b.Property(x => x.SerialMaskSnapshot).HasMaxLength(250);
        b.HasOne<Modules.SerialNumberPolicy.Domain.SerialNumberRule>().WithMany().HasForeignKey(x => x.SerialNumberRuleId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ManufacturingDate).HasColumnType("date");
        b.Property(x => x.ExpirationDate).HasColumnType("date");
        b.Property(x => x.ScannedBarcode).HasMaxLength(250);
        b.Property(x => x.StockStatus).HasMaxLength(30).IsRequired();
        b.HasOne(x => x.Execution).WithMany(x => x.Lines).HasForeignKey(x => x.GrExecutionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.GrLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Modules.Stock.Domain.Stock>().WithMany().HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Modules.YapCode.Domain.YapCode>().WithMany().HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<GoodsReceiptLabel>().WithMany().HasForeignKey(x => x.GoodsReceiptLabelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Modules.Quality.Domain.QualityInspectionLine>().WithMany().HasForeignKey(x => x.QualityInspectionLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.GrExecutionId, x.LineNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_GR_EXECUTION_LINE_SEQUENCE");
        b.HasIndex(x => new { x.GrLineId, x.GrExecutionId }).HasDatabaseName("IX_RII_GR_EXECUTION_LINE_GR_LINE");
        b.HasIndex(x => new { x.StockId, x.LotNo, x.SerialNo }).HasDatabaseName("IX_RII_GR_EXECUTION_LINE_TRACE");
    }
}
