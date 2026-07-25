using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.YapCode.Infrastructure;

public sealed class YapCodeConfiguration : BaseEntityConfiguration<Domain.YapCode>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Domain.YapCode> builder)
    {
        builder.ToTable("RII_YAP_CODE");
        builder.Property(x => x.ConfigurationCode).HasMaxLength(15).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ConfigurableStockCode).HasMaxLength(35);
        builder.HasOne(x => x.Stock).WithMany().HasForeignKey(x => x.StockId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.BranchCode, x.ConfigurationCode })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_YapCode_BranchCode_ConfigurationCode");
        builder.HasIndex(x => x.Description).HasDatabaseName("IX_YapCode_Description");
        builder.HasIndex(x => x.StockId).HasDatabaseName("IX_YapCode_StockId");
    }
}
