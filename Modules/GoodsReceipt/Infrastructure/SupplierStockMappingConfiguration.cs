using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;

public sealed class SupplierStockMappingConfiguration : BaseEntityConfiguration<SupplierStockMapping>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SupplierStockMapping> builder)
    {
        builder.ToTable("RII_SUPPLIER_STOCK_MAPPING");
        builder.Property(x => x.SupplierStockCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedSupplierStockCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SupplierStockName).HasMaxLength(500);
        builder.Property(x => x.SupplierUnitCode).HasMaxLength(20);
        builder.Property(x => x.ConversionFactor).HasPrecision(28, 8).HasDefaultValue(1m);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne<Modules.Customer.Domain.Customer>().WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Stock.Domain.Stock>().WithMany()
            .HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
            {
                x.BranchCode,
                x.SupplierId,
                x.NormalizedSupplierStockCode
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_SUPPLIER_STOCK_MAPPING_IDENTITY");
        builder.HasIndex(x => new { x.BranchCode, x.SupplierId, x.IsActive })
            .HasDatabaseName("IX_RII_SUPPLIER_STOCK_MAPPING_SUPPLIER_ACTIVE");
        builder.HasIndex(x => new { x.BranchCode, x.StockId, x.IsActive })
            .HasDatabaseName("IX_RII_SUPPLIER_STOCK_MAPPING_STOCK_ACTIVE");
    }
}
