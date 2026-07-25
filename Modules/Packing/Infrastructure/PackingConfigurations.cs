using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Packing.Infrastructure;

public sealed class PackagingMaterialConfiguration : BaseEntityConfiguration<PackagingMaterial>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PackagingMaterial> b)
    {
        b.ToTable("RII_PACKAGING_MATERIAL", t => t.HasCheckConstraint("CK_RII_PACKAGING_MATERIAL_CAPACITY", "[TareWeight] >= 0 AND ([MaxNetWeight] IS NULL OR [MaxNetWeight] > 0) AND ([MaxGrossWeight] IS NULL OR [MaxGrossWeight] > 0)"));
        b.Property(x=>x.Code).HasMaxLength(50).IsRequired(); b.Property(x=>x.Name).HasMaxLength(150).IsRequired();
        b.Property(x=>x.Type).HasConversion<string>().HasMaxLength(30); b.Property(x=>x.Description).HasMaxLength(500);
        foreach(var p in new[]{nameof(PackagingMaterial.TareWeight),nameof(PackagingMaterial.MaxNetWeight),nameof(PackagingMaterial.MaxGrossWeight),nameof(PackagingMaterial.InnerLength),nameof(PackagingMaterial.InnerWidth),nameof(PackagingMaterial.InnerHeight),nameof(PackagingMaterial.MaxVolume)}) b.Property(p).HasPrecision(20,6);
        b.Property(x=>x.RowVersion).IsRowVersion(); b.HasIndex(x=>new{x.BranchCode,x.Code}).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
public sealed class PackingStationConfiguration : BaseEntityConfiguration<PackingStation>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PackingStation>b){b.ToTable("RII_PACKING_STATION");b.Property(x=>x.Code).HasMaxLength(50).IsRequired();b.Property(x=>x.Name).HasMaxLength(150).IsRequired();b.Property(x=>x.ScaleDeviceCode).HasMaxLength(100);b.Property(x=>x.Description).HasMaxLength(500);b.Property(x=>x.RowVersion).IsRowVersion();b.HasIndex(x=>new{x.BranchCode,x.WarehouseId,x.Code}).IsUnique().HasFilter("[IsDeleted] = 0");}
}
public sealed class PackingPolicyConfiguration : BaseEntityConfiguration<PackingPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PackingPolicy>b){b.ToTable("RII_PACKING_POLICY",t=>t.HasCheckConstraint("CK_RII_PACKING_POLICY_WEIGHT_TOLERANCE","[WeightTolerancePercent] BETWEEN 0 AND 100"));b.Property(x=>x.PolicyKey).HasMaxLength(30).IsRequired();b.Property(x=>x.WeightTolerancePercent).HasPrecision(9,4);b.Property(x=>x.ClosePolicy).HasConversion<string>().HasMaxLength(30);b.Property(x=>x.ReleasePolicy).HasConversion<string>().HasMaxLength(30);b.Property(x=>x.RowVersion).IsRowVersion();b.HasIndex(x=>new{x.BranchCode,x.PolicyKey}).IsUnique().HasFilter("[IsDeleted] = 0");}
}
public sealed class PackagingSpecificationConfiguration : BaseEntityConfiguration<PackagingSpecification>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PackagingSpecification>b){b.ToTable("RII_PACKAGING_SPECIFICATION");b.Property(x=>x.StockGroupCode).HasMaxLength(100);b.Property(x=>x.UnitsPerHandlingUnit).HasPrecision(20,6);b.Property(x=>x.MaxNetWeight).HasPrecision(20,6);b.Property(x=>x.MaxVolume).HasPrecision(20,6);b.Property(x=>x.Notes).HasMaxLength(500);b.HasIndex(x=>new{x.BranchCode,x.StockId,x.StockGroupCode,x.CustomerId,x.Priority});}
}
public sealed class PackingSessionConfiguration : BaseEntityConfiguration<PackingSession>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PackingSession>b){b.ToTable("RII_PACKING_HEADER");b.Property(x=>x.PackingNo).HasMaxLength(50).IsRequired();b.Property(x=>x.SourceType).HasConversion<string>().HasMaxLength(30);b.Property(x=>x.SourceDocumentNo).HasMaxLength(100);b.Property(x=>x.CustomerCodeSnapshot).HasMaxLength(100);b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30);b.Property(x=>x.Notes).HasMaxLength(1000);b.Property(x=>x.RowVersion).IsRowVersion();b.HasIndex(x=>new{x.BranchCode,x.PackingNo}).IsUnique();b.HasIndex(x=>new{x.SourceType,x.SourceHeaderId});b.HasIndex(x=>x.IdempotencyKey).IsUnique();}
}
public sealed class HandlingUnitConfiguration : BaseEntityConfiguration<HandlingUnit>
{
    protected override void ConfigureEntity(EntityTypeBuilder<HandlingUnit>b){b.ToTable("RII_HANDLING_UNIT",t=>t.HasCheckConstraint("CK_RII_HANDLING_UNIT_WEIGHT","[TareWeight] >= 0 AND [NetWeight] >= 0 AND [GrossWeight] >= 0"));b.Property(x=>x.HandlingUnitNo).HasMaxLength(100).IsRequired();b.Property(x=>x.Sscc).HasMaxLength(18);b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30);foreach(var p in new[]{nameof(HandlingUnit.TareWeight),nameof(HandlingUnit.NetWeight),nameof(HandlingUnit.MeasuredGrossWeight),nameof(HandlingUnit.GrossWeight),nameof(HandlingUnit.Length),nameof(HandlingUnit.Width),nameof(HandlingUnit.Height),nameof(HandlingUnit.Volume)})b.Property(p).HasPrecision(20,6);b.Property(x=>x.RowVersion).IsRowVersion();b.HasOne(x=>x.Session).WithMany(x=>x.HandlingUnits).HasForeignKey(x=>x.PackingSessionId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.Parent).WithMany(x=>x.Children).HasForeignKey(x=>x.ParentHandlingUnitId).OnDelete(DeleteBehavior.Restrict);b.HasIndex(x=>new{x.BranchCode,x.HandlingUnitNo}).IsUnique();b.HasIndex(x=>x.Sscc).IsUnique().HasFilter("[Sscc] IS NOT NULL");}
}
public sealed class HandlingUnitLineConfiguration : BaseEntityConfiguration<HandlingUnitLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<HandlingUnitLine>b){b.ToTable("RII_HANDLING_UNIT_LINE",t=>t.HasCheckConstraint("CK_RII_HANDLING_UNIT_LINE_QUANTITY","[Quantity] > 0"));b.Property(x=>x.StockCodeSnapshot).HasMaxLength(100).IsRequired();b.Property(x=>x.YapCodeSnapshot).HasMaxLength(100);b.Property(x=>x.UnitCode).HasMaxLength(20);b.Property(x=>x.Quantity).HasPrecision(20,6);b.Property(x=>x.LotNo).HasMaxLength(100);b.Property(x=>x.SerialNo).HasMaxLength(200);b.HasOne(x=>x.HandlingUnit).WithMany(x=>x.Lines).HasForeignKey(x=>x.HandlingUnitId).OnDelete(DeleteBehavior.Restrict);b.HasIndex(x=>new{x.SourceLineId,x.LotNo,x.SerialNo});}
}
public sealed class PackingEventConfiguration : BaseEntityConfiguration<PackingEvent>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PackingEvent>b){b.ToTable("RII_PACKING_EVENT");b.Property(x=>x.EventType).HasMaxLength(50);b.Property(x=>x.FromStatus).HasMaxLength(30);b.Property(x=>x.ToStatus).HasMaxLength(30);b.Property(x=>x.Description).HasMaxLength(1000);b.HasIndex(x=>new{x.PackingSessionId,x.IdempotencyKey}).IsUnique();b.HasIndex(x=>new{x.PackingSessionId,x.OccurredAtUtc});}
}
public sealed class PackingPrintJobConfiguration : BaseEntityConfiguration<PackingPrintJob>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PackingPrintJob>b)
    {
        b.ToTable("RII_PACKING_PRINT_JOB",t=>t.HasCheckConstraint("CK_RII_PACKING_PRINT_JOB_COPIES","[Copies] > 0 AND [AttemptCount] >= 0"));
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x=>x.LastError).HasMaxLength(2000);
        b.HasOne<HandlingUnit>().WithMany().HasForeignKey(x=>x.HandlingUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PackingStation>().WithMany().HasForeignKey(x=>x.PackingStationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.IdempotencyKey).IsUnique();
        b.HasIndex(x=>new{x.Status,x.NextAttemptAtUtc,x.RequestedAtUtc});
        b.HasIndex(x=>x.HandlingUnitId);
    }
}
public sealed class PackingScaleReadingConfiguration : BaseEntityConfiguration<PackingScaleReading>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PackingScaleReading>b)
    {
        b.ToTable("RII_PACKING_SCALE_READING",t=>t.HasCheckConstraint("CK_RII_PACKING_SCALE_READING_WEIGHT","[GrossWeight] > 0"));
        b.Property(x=>x.DeviceCode).HasMaxLength(100).IsRequired();
        b.Property(x=>x.GrossWeight).HasPrecision(20,6);
        b.Property(x=>x.RawPayload).HasMaxLength(2000);
        b.HasOne<PackingStation>().WithMany().HasForeignKey(x=>x.PackingStationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HandlingUnit>().WithMany().HasForeignKey(x=>x.HandlingUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.IdempotencyKey).IsUnique();
        b.HasIndex(x=>new{x.PackingStationId,x.CapturedAtUtc});
        b.HasIndex(x=>new{x.HandlingUnitId,x.CapturedAtUtc});
    }
}
