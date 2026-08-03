using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Stock.Infrastructure;

public sealed class StockImageConfiguration : BaseEntityConfiguration<StockImage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StockImage> builder)
    {
        builder.ToTable("RII_STOCK_IMAGE");
        builder.Property(x => x.FileUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AltText).HasMaxLength(200);
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.IsPrimary).IsRequired().HasDefaultValue(false);
        builder.HasOne(x => x.Stock).WithMany(x => x.Images).HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BranchCode, x.StockId, x.SortOrder })
            .HasFilter("[IsDeleted] = 0").IsUnique().HasDatabaseName("UX_StockImage_Branch_Stock_SortOrder");
        builder.HasIndex(x => new { x.BranchCode, x.StockId })
            .HasFilter("[IsDeleted] = 0 AND [IsPrimary] = 1").IsUnique().HasDatabaseName("UX_StockImage_OnePrimary");
    }
}
