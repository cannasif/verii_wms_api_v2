using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Stock.Infrastructure;

public sealed class StockConfiguration : BaseEntityConfiguration<Domain.Stock>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Domain.Stock> builder)
    {
        builder.ToTable("RII_STOCK");
        builder.Property(x => x.ErpStockCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.StockName).HasMaxLength(250).IsRequired();
        builder.Property(x => x.BaseUnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ManufacturerCode).HasMaxLength(50);
        builder.Property(x => x.GroupCode).HasMaxLength(50);
        builder.Property(x => x.Code1).HasMaxLength(50);
        builder.Property(x => x.Code2).HasMaxLength(50);
        builder.Property(x => x.Code3).HasMaxLength(50);
        builder.Property(x => x.Code4).HasMaxLength(50);
        builder.Property(x => x.Code5).HasMaxLength(50);
        builder.HasIndex(x => new { x.BranchCode, x.ErpStockCode })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Stock_BranchCode_ErpStockCode");
        builder.HasIndex(x => x.StockName).HasDatabaseName("IX_Stock_StockName");
    }
}
