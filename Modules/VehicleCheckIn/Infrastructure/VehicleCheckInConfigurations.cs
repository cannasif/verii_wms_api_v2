using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.VehicleCheckIn.Infrastructure;

public sealed class VehicleCheckInHeaderConfiguration:BaseEntityConfiguration<VehicleCheckInHeader>
{
    protected override void ConfigureEntity(EntityTypeBuilder<VehicleCheckInHeader> b)
    {
        b.ToTable("RII_VEHICLE_CHECKIN_HEADER");
        b.Property(x=>x.PlateNo).HasMaxLength(25).IsRequired(); b.Property(x=>x.PlateNoNormalized).HasMaxLength(25).IsRequired();
        b.Property(x=>x.TrailerPlateNo).HasMaxLength(25); b.Property(x=>x.TrailerPlateNoNormalized).HasMaxLength(25);
        b.Property(x=>x.DriverFirstName).HasMaxLength(100); b.Property(x=>x.DriverLastName).HasMaxLength(100);
        b.Property(x=>x.DriverPhone).HasMaxLength(40); b.Property(x=>x.CarrierName).HasMaxLength(200);
        b.Property(x=>x.CustomerCodeSnapshot).HasMaxLength(100); b.Property(x=>x.CustomerNameSnapshot).HasMaxLength(300);
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30); b.Property(x=>x.Note).HasMaxLength(1000);
        b.Property(x=>x.BusinessDate).HasColumnType("date"); b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>new{x.BranchCode,x.PlateNoNormalized,x.BusinessDate}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.BranchCode,x.CheckedInAtUtc}); b.HasIndex(x=>x.CustomerId);
        b.HasOne<verii_wms_api_v2.Modules.Customer.Domain.Customer>().WithMany().HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x=>x.Images).WithOne(x=>x.Header).HasForeignKey(x=>x.HeaderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class VehicleCheckInImageConfiguration:BaseEntityConfiguration<VehicleCheckInImage>
{
    protected override void ConfigureEntity(EntityTypeBuilder<VehicleCheckInImage> b)
    {
        b.ToTable("RII_VEHICLE_CHECKIN_IMAGE");
        b.Property(x=>x.FileName).HasMaxLength(260).IsRequired(); b.Property(x=>x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x=>x.StoragePath).HasMaxLength(500).IsRequired();
        b.HasIndex(x=>new{x.HeaderId,x.SortOrder});
    }
}
