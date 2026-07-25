using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Customer.Infrastructure;

public sealed class CustomerConfiguration : BaseEntityConfiguration<Domain.Customer>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Domain.Customer> builder)
    {
        builder.ToTable("RII_CUSTOMER");
        builder.Property(x => x.CustomerCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.BranchCode, x.CustomerCode })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Customer_BranchCode_CustomerCode");
        builder.HasIndex(x => x.CustomerName).HasDatabaseName("IX_Customer_CustomerName");
    }
}
