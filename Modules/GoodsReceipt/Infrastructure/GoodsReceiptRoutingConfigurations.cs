using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;

public sealed class GoodsReceiptRoutingBatchConfiguration : BaseEntityConfiguration<GoodsReceiptRoutingBatch>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptRoutingBatch> builder)
    {
        builder.ToTable("RII_GR_ROUTING_BATCH");
        builder.Property(x => x.RouteType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.TargetDocumentNo).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RoutedAtUtc).HasColumnType("datetimeoffset(7)").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasOne(x => x.Header).WithMany()
            .HasForeignKey(x => x.GrHeaderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CorrelationId).IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_GR_ROUTING_BATCH_CORRELATION");
        builder.HasIndex(x => new { x.GrHeaderId, x.RouteType, x.RoutedAtUtc })
            .HasDatabaseName("IX_RII_GR_ROUTING_BATCH_HEADER_TYPE_DATE");
    }
}

public sealed class GoodsReceiptRoutingAllocationConfiguration : BaseEntityConfiguration<GoodsReceiptRoutingAllocation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptRoutingAllocation> builder)
    {
        builder.ToTable("RII_GR_ROUTING_ALLOCATION", table =>
            table.HasCheckConstraint("CK_RII_GR_ROUTING_ALLOCATION_QUANTITY", "[Quantity] > 0"));
        builder.Property(x => x.Quantity).HasPrecision(18, 6).IsRequired();
        builder.HasOne(x => x.RoutingBatch).WithMany(x => x.Allocations)
            .HasForeignKey(x => x.RoutingBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.GoodsReceiptLine).WithMany()
            .HasForeignKey(x => x.GrLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.RoutingBatchId, x.GrLineId }).IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_GR_ROUTING_ALLOCATION_BATCH_LINE");
        builder.HasIndex(x => new { x.GrLineId, x.RoutingBatchId })
            .HasDatabaseName("IX_RII_GR_ROUTING_ALLOCATION_LINE_BATCH");
    }
}
