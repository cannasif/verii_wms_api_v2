using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared.Infrastructure;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Quality.Infrastructure;

public sealed class QualityParameterConfiguration : BaseEntityConfiguration<QualityParameter>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QualityParameter> b)
    {
        b.ToTable("RII_QUALITY_PARAMETERS"); b.Property(x => x.ParameterKey).HasMaxLength(30).IsRequired();
        b.Property(x => x.DefaultInspectionMode).HasConversion<string>().HasMaxLength(30); b.Property(x => x.DefaultFailAction).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.RowVersion).IsRowVersion(); b.HasIndex(x => new { x.BranchCode, x.ParameterKey }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.DefaultAcceptedLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.QuarantineDestinations).WithOne(x => x.QualityParameter)
            .HasForeignKey(x => x.QualityParameterId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.WarehouseRoutes).WithOne(x => x.QualityParameter)
            .HasForeignKey(x => x.QualityParameterId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class QualityWarehouseRouteConfiguration : BaseEntityConfiguration<QualityWarehouseRoute>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QualityWarehouseRoute> b)
    {
        b.ToTable("RII_QUALITY_WAREHOUSE_ROUTES", table =>
        {
            table.HasCheckConstraint(
                "CK_RII_QUALITY_WAREHOUSE_ROUTE_TARGET",
                "[QualityLocationId] IS NOT NULL OR [AcceptedLocationId] IS NOT NULL OR [QuarantineLocationId] IS NOT NULL OR [RejectLocationId] IS NOT NULL");
        });
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.QualityParameterId, x.SourceWarehouseId })
            .IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.BranchCode, x.SourceWarehouseId, x.IsActive });
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.SourceWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.QualityLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.AcceptedLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.QuarantineLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.RejectLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class QualityQuarantineDestinationConfiguration : BaseEntityConfiguration<QualityQuarantineDestination>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QualityQuarantineDestination> b)
    {
        b.ToTable("RII_QUALITY_QUARANTINE_DESTINATIONS");
        b.Property(x => x.Priority).HasDefaultValue(100);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.QualityParameterId, x.LocationId })
            .IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.QualityParameterId, x.IsActive, x.Priority });
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
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
        b.HasMany(x => x.Dispositions).WithOne(x => x.QualityInspection).HasForeignKey(x => x.QualityInspectionId).OnDelete(DeleteBehavior.Restrict);
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
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.QuarantineLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Dispositions).WithOne(x => x.QualityInspectionLine).HasForeignKey(x => x.QualityInspectionLineId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class QualityInspectionDispositionConfiguration : BaseEntityConfiguration<QualityInspectionDisposition>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QualityInspectionDisposition> b)
    {
        b.ToTable("RII_QUALITY_INSPECTION_DISPOSITIONS", table =>
        {
            table.HasCheckConstraint("CK_RII_QUALITY_DISPOSITION_QUANTITY", "[Quantity] > 0");
            table.HasCheckConstraint("CK_RII_QUALITY_DISPOSITION_SEQUENCE", "[SequenceNo] > 0");
        });
        b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Quantity).HasPrecision(18, 6);
        b.Property(x => x.SourceWarehouseCodeSnapshot).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceLocationCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x => x.TargetWarehouseCodeSnapshot).HasMaxLength(50).IsRequired();
        b.Property(x => x.TargetLocationCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceStockStatus).HasMaxLength(30).IsRequired();
        b.Property(x => x.TargetStockStatus).HasMaxLength(30).IsRequired();
        b.Property(x => x.ReasonCode).HasMaxLength(100);
        b.Property(x => x.ReasonNote).HasMaxLength(1000);
        b.HasIndex(x => new { x.QualityInspectionId, x.IdempotencyKey, x.SequenceNo })
            .IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.QualityInspectionLineId, x.Decision, x.DecisionAtUtc });
        b.HasIndex(x => x.WarehouseTransferId);
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseEntity>().WithMany().HasForeignKey(x => x.TargetWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.TargetLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
