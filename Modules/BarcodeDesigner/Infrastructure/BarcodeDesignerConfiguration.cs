using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Infrastructure;

public sealed class BarcodeTemplateConfiguration : BaseEntityConfiguration<BarcodeTemplate>
{
    protected override void ConfigureEntity(EntityTypeBuilder<BarcodeTemplate> builder)
    {
        builder.ToTable("RII_BARCODE_TEMPLATE", table =>
        {
            table.HasCheckConstraint("CK_RII_BARCODE_TEMPLATE_SIZE", "[WidthMm] BETWEEN 10 AND 300 AND [HeightMm] BETWEEN 10 AND 500");
            table.HasCheckConstraint("CK_RII_BARCODE_TEMPLATE_DPI", "[Dpi] IN (203,300,600)");
        });
        builder.Property(x => x.TemplateCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.LabelType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.WidthMm).HasColumnType("decimal(8,2)");
        builder.Property(x => x.HeightMm).HasColumnType("decimal(8,2)");
        builder.Property(x => x.EngineType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(x => new { x.BranchCode, x.TemplateCode }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_BARCODE_TEMPLATE_BRANCH_CODE");
        builder.HasIndex(x => new { x.LabelType, x.IsActive }).HasDatabaseName("IX_RII_BARCODE_TEMPLATE_TYPE_ACTIVE");
    }
}

public sealed class BarcodeTemplateVersionConfiguration : BaseEntityConfiguration<BarcodeTemplateVersion>
{
    protected override void ConfigureEntity(EntityTypeBuilder<BarcodeTemplateVersion> builder)
    {
        builder.ToTable("RII_BARCODE_TEMPLATE_VERSION");
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.TemplateJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasOne<BarcodeTemplate>().WithMany().HasForeignKey(x => x.BarcodeTemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BarcodeTemplateId, x.VersionNo }).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_BARCODE_TEMPLATE_VERSION_NO");
        builder.HasIndex(x => new { x.BarcodeTemplateId, x.IsPublished }).HasDatabaseName("IX_RII_BARCODE_TEMPLATE_VERSION_PUBLISHED");
    }
}

public sealed class BarcodePolicyConfiguration : BaseEntityConfiguration<BarcodePolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<BarcodePolicy> builder)
    {
        builder.ToTable("RII_BARCODE_POLICY", table => table.HasCheckConstraint("CK_RII_BARCODE_POLICY_VERSION", "[CurrentVersion] > 0"));
        builder.Property(x=>x.PolicyKey).HasMaxLength(30).IsRequired(); builder.Property(x=>x.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(x=>x.IsActive).HasDefaultValue(true); builder.Property(x=>x.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(x=>new{x.BranchCode,x.PolicyKey}).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_BARCODE_POLICY_BRANCH_KEY");
    }
}

public sealed class BarcodePolicyProfileConfiguration : BaseEntityConfiguration<BarcodePolicyProfile>
{
    protected override void ConfigureEntity(EntityTypeBuilder<BarcodePolicyProfile> builder)
    {
        builder.ToTable("RII_BARCODE_POLICY_PROFILE",table=>table.HasCheckConstraint("CK_RII_BARCODE_POLICY_PROFILE_SEQUENCE","[NextSequence] > 0"));
        builder.Property(x=>x.Scope).HasConversion<string>().HasMaxLength(30).IsRequired();builder.Property(x=>x.DisplayName).HasMaxLength(150).IsRequired();builder.Property(x=>x.Prefix).HasMaxLength(30);builder.Property(x=>x.Separator).HasMaxLength(5).IsRequired();builder.Property(x=>x.IsEnabled).HasDefaultValue(true);builder.Property(x=>x.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasOne<BarcodePolicy>().WithMany().HasForeignKey(x=>x.BarcodePolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x=>new{x.BarcodePolicyId,x.Scope}).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_BARCODE_POLICY_PROFILE_SCOPE");
    }
}

public sealed class BarcodePolicyProfileSegmentConfiguration : BaseEntityConfiguration<BarcodePolicyProfileSegment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<BarcodePolicyProfileSegment> builder)
    {
        builder.ToTable("RII_BARCODE_POLICY_PROFILE_SEGMENT"); builder.Property(x=>x.SegmentType).HasConversion<string>().HasMaxLength(20).IsRequired(); builder.Property(x=>x.SourceField).HasConversion<string>().HasMaxLength(30);
        builder.Property(x=>x.LiteralValue).HasMaxLength(50); builder.Property(x=>x.Transform).HasConversion<string>().HasMaxLength(20).IsRequired(); builder.Property(x=>x.DateFormat).HasMaxLength(20).IsRequired();
        builder.HasOne<BarcodePolicyProfile>().WithMany().HasForeignKey(x=>x.BarcodePolicyProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x=>new{x.BarcodePolicyProfileId,x.Order}).IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UX_RII_BARCODE_POLICY_PROFILE_SEGMENT_ORDER");
    }
}

public sealed class GeneratedBarcodeConfiguration : BaseEntityConfiguration<GeneratedBarcode>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GeneratedBarcode> builder)
    {
        builder.ToTable("RII_GENERATED_BARCODE"); builder.Property(x=>x.BarcodeValue).HasMaxLength(120).IsRequired(); builder.Property(x=>x.BarcodeHash).HasMaxLength(64).IsRequired(); builder.Property(x=>x.IdempotencyHash).HasMaxLength(64).IsRequired();
        builder.Property(x=>x.Scope).HasConversion<string>().HasMaxLength(30).IsRequired();builder.Property(x=>x.StockCode).HasMaxLength(50); builder.Property(x=>x.SerialNo).HasMaxLength(100); builder.Property(x=>x.YapCode).HasMaxLength(100); builder.Property(x=>x.LotNo).HasMaxLength(100);builder.Property(x=>x.WarehouseCode).HasMaxLength(50);builder.Property(x=>x.LocationCode).HasMaxLength(100);builder.Property(x=>x.DocumentNo).HasMaxLength(100);
        builder.HasOne<BarcodePolicy>().WithMany().HasForeignKey(x=>x.BarcodePolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BarcodePolicyProfile>().WithMany().HasForeignKey(x=>x.BarcodePolicyProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x=>x.BarcodeHash).IsUnique().HasDatabaseName("UX_RII_GENERATED_BARCODE_HASH");
        builder.HasIndex(x=>new{x.BarcodePolicyId,x.Scope,x.IdempotencyHash}).IsUnique().HasDatabaseName("UX_RII_GENERATED_BARCODE_IDEMPOTENCY");
    }
}
