using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.Quality.Infrastructure;

public sealed class QualityParameterConfiguration : BaseEntityConfiguration<QualityParameter>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QualityParameter> b)
    {
        b.ToTable("RII_QUALITY_PARAMETERS"); b.Property(x => x.ParameterKey).HasMaxLength(30).IsRequired();
        b.Property(x => x.DefaultInspectionMode).HasConversion<string>().HasMaxLength(30); b.Property(x => x.DefaultFailAction).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RowVersion).IsRowVersion(); b.HasIndex(x => new { x.BranchCode, x.ParameterKey }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class QualityRuleConfiguration : BaseEntityConfiguration<QualityRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QualityRule> b)
    {
        b.ToTable("RII_QUALITY_RULES"); b.Property(x => x.ScopeType).HasMaxLength(30).IsRequired(); b.Property(x => x.StockGroupCode).HasMaxLength(50);
        b.Property(x => x.InspectionMode).HasConversion<string>().HasMaxLength(30); b.Property(x => x.SamplingMode).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.SamplingValue).HasPrecision(18, 6); b.Property(x => x.FailAction).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Description).HasMaxLength(500); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.ScopeType, x.StockId, x.StockGroupCode, x.IsActive });
    }
}

public sealed class QualityInspectionConfiguration : BaseEntityConfiguration<QualityInspection>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QualityInspection> b)
    {
        b.ToTable("RII_QUALITY_INSPECTIONS"); b.Property(x => x.InspectionNo).HasMaxLength(60).IsRequired(); b.Property(x => x.SourceDocumentType).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceDocumentNo).HasMaxLength(100).IsRequired(); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30); b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion(); b.HasIndex(x => x.CorrelationId).IsUnique(); b.HasIndex(x => new { x.BranchCode, x.Status, x.CreatedAtUtc });
        b.HasIndex(x => new { x.BranchCode, x.QueuedAtUtc, x.Status });
        b.HasMany(x => x.Lines).WithOne(x => x.Inspection).HasForeignKey(x => x.QualityInspectionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class QualityInspectionLineConfiguration : BaseEntityConfiguration<QualityInspectionLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QualityInspectionLine> b)
    {
        b.ToTable("RII_QUALITY_INSPECTION_LINES"); b.Property(x => x.StockCodeSnapshot).HasMaxLength(100).IsRequired(); b.Property(x => x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x => x.YapCodeSnapshot).HasMaxLength(100); b.Property(x => x.LotNo).HasMaxLength(100); b.Property(x => x.SerialNo).HasMaxLength(100);
        foreach (var p in new[] { nameof(QualityInspectionLine.Quantity), nameof(QualityInspectionLine.SampleQuantity), nameof(QualityInspectionLine.AcceptedQuantity), nameof(QualityInspectionLine.RejectedQuantity), nameof(QualityInspectionLine.QuarantineQuantity) }) b.Property(p).HasPrecision(18, 6);
        b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(30); b.Property(x => x.ReasonCode).HasMaxLength(100); b.Property(x => x.ReasonNote).HasMaxLength(1000); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.GoodsReceiptLineId); b.HasIndex(x => x.WarehouseInboundLineId); b.HasIndex(x => new { x.StockId, x.LotNo, x.SerialNo });
    }
}
