using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;

public sealed class GoodsReceiptHeaderConfiguration : BaseEntityConfiguration<GoodsReceiptHeader>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptHeader> builder)
    {
        builder.ToTable("RII_GR_HEADER", table =>
        {
            table.HasCheckConstraint("CK_RII_GR_HEADER_PRIORITY", "[Priority] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_RII_GR_HEADER_OVER_TOLERANCE", "[OverReceiptTolerancePercent] BETWEEN 0 AND 100");
        });

        builder.Property(x => x.DocumentNo).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DocumentDate).HasColumnType("date").IsRequired();
        Enum(builder.Property(x => x.ReceiptType), 30);
        Enum(builder.Property(x => x.InitiationMode), 30);
        Enum(builder.Property(x => x.ProcessType), 40);
        Enum(builder.Property(x => x.LabelStrategy), 30);
        builder.Property(x => x.InitiationMode)
            .HasDefaultValue(GoodsReceiptInitiationMode.OrderBasedTask)
            .HasSentinel((GoodsReceiptInitiationMode)0);
        builder.Property(x => x.ProcessType)
            .HasDefaultValue(GoodsReceiptProcessType.OrderBasedTask)
            .HasSentinel((GoodsReceiptProcessType)0);
        builder.Property(x => x.LabelStrategy)
            .HasDefaultValue(GoodsReceiptLabelStrategy.None)
            .HasSentinel((GoodsReceiptLabelStrategy)0);
        Enum(builder.Property(x => x.SourceSystem), 30);
        builder.Property(x => x.ExternalReferenceNo).HasMaxLength(100);
        builder.Property(x => x.SupplierCodeSnapshot).HasMaxLength(50);
        builder.Property(x => x.SupplierNameSnapshot).HasMaxLength(200);
        builder.Property(x => x.SupplierTaxNoSnapshot).HasMaxLength(20);
        builder.Property(x => x.DefaultPutawayZoneCode).HasMaxLength(50);
        Enum(builder.Property(x => x.Status), 30);
        Enum(builder.Property(x => x.ApprovalStatus), 30);
        Enum(builder.Property(x => x.QualityStatus), 30);
        Enum(builder.Property(x => x.PutawayStatus), 30);
        Enum(builder.Property(x => x.ErpIntegrationStatus), 30);
        Enum(builder.Property(x => x.OverReceiptPolicy), 30);
        Enum(builder.Property(x => x.InventoryAvailabilityPolicy), 40);
        Enum(builder.Property(x => x.ErpPostingPolicy), 40);
        builder.Property(x => x.OverReceiptPolicy).HasDefaultValue(OverReceiptPolicy.NotAllowed).HasSentinel((OverReceiptPolicy)0);
        builder.Property(x => x.InventoryAvailabilityPolicy).HasDefaultValue(InventoryAvailabilityPolicy.AfterQualityApproval).HasSentinel((InventoryAvailabilityPolicy)0);
        builder.Property(x => x.ErpPostingPolicy).HasDefaultValue(GoodsReceiptErpPostingPolicy.AfterAllApprovals).HasSentinel((GoodsReceiptErpPostingPolicy)0);

        Utc(builder.Property(x => x.PlannedArrivalAtUtc));
        Utc(builder.Property(x => x.ActualArrivalAtUtc));
        Utc(builder.Property(x => x.ReleasedAtUtc));
        Utc(builder.Property(x => x.StartedAtUtc));
        Utc(builder.Property(x => x.ReceivedAtUtc));
        Utc(builder.Property(x => x.CompletedAtUtc));
        Utc(builder.Property(x => x.CancelledAtUtc));

        builder.Property(x => x.CancellationReason).HasMaxLength(500);
        builder.Property(x => x.WaybillNo).HasMaxLength(15).IsUnicode(false);
        builder.Property(x => x.WaybillDate).HasColumnType("date");
        // Legacy 16-character records remain readable; new purchase e-waybill/GİB
        // references are constrained to exactly 15 characters at the application boundary.
        builder.Property(x => x.ElectronicWaybillNo).HasMaxLength(16).IsUnicode(false);
        builder.Property(x => x.ShipmentReferenceNo).HasMaxLength(100);
        builder.Property(x => x.CarrierCode).HasMaxLength(50);
        builder.Property(x => x.CarrierName).HasMaxLength(200);
        builder.Property(x => x.VehiclePlate).HasMaxLength(20);
        builder.Property(x => x.TrailerPlate).HasMaxLength(20);
        builder.Property(x => x.DriverName).HasMaxLength(150);
        builder.Property(x => x.SealNo).HasMaxLength(50);
        builder.Property(x => x.OverReceiptTolerancePercent).HasPrecision(9, 4);
        builder.Property(x => x.Priority).HasDefaultValue((byte)3);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.BranchCode, x.DocumentNo })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_GR_HEADER_BRANCH_DOCUMENT_NO");
        builder.HasIndex(x => x.CorrelationId).IsUnique()
            .HasDatabaseName("UX_RII_GR_HEADER_CORRELATION_ID");
        builder.HasIndex(x => new { x.BranchCode, x.Status, x.PlannedArrivalAtUtc })
            .HasDatabaseName("IX_RII_GR_HEADER_BRANCH_STATUS_PLANNED");
        builder.HasIndex(x => new { x.BranchCode, x.ProcessType, x.Status, x.DocumentDate })
            .HasDatabaseName("IX_RII_GR_HEADER_PROCESS_REPORTING");
        builder.HasIndex(x => new { x.SupplierId, x.Status })
            .HasDatabaseName("IX_RII_GR_HEADER_SUPPLIER_STATUS");
        builder.HasIndex(x => new { x.TargetWarehouseId, x.Status })
            .HasDatabaseName("IX_RII_GR_HEADER_WAREHOUSE_STATUS");
        builder.HasIndex(x => new { x.BranchCode, x.SupplierId, x.WaybillNo })
            .IsUnique().HasFilter("[IsDeleted] = 0 AND [WaybillNo] IS NOT NULL")
            .HasDatabaseName("UX_RII_GR_HEADER_SUPPLIER_WAYBILL");
        builder.HasIndex(x => new { x.BranchCode, x.SupplierId, x.ElectronicWaybillNo })
            .IsUnique().HasFilter("[IsDeleted] = 0 AND [ElectronicWaybillNo] IS NOT NULL")
            .HasDatabaseName("UX_RII_GR_HEADER_SUPPLIER_EWAYBILL");

        builder.HasOne<Modules.DocumentSeries.Domain.DocumentSeries>().WithMany()
            .HasForeignKey(x => x.DocumentSeriesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Customer.Domain.Customer>().WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany()
            .HasForeignKey(x => x.TargetWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany()
            .HasForeignKey(x => x.ReceivingLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany()
            .HasForeignKey(x => x.QualityLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany()
            .HasForeignKey(x => x.QuarantineLocationId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void Enum<T>(PropertyBuilder<T> property, int length) where T : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(length).IsRequired();

    private static void Utc(PropertyBuilder<DateTimeOffset?> property) => property.HasColumnType("datetimeoffset(7)");
}

public sealed class GoodsReceiptSourceDocumentConfiguration : BaseEntityConfiguration<GoodsReceiptSourceDocument>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptSourceDocument> builder)
    {
        builder.ToTable("RII_GR_SOURCE_DOCUMENT");
        builder.Property(x => x.SourceDocumentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.SourceSystem).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.ExternalDocumentId).HasMaxLength(100);
        builder.Property(x => x.ExternalDocumentNo).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExternalDocumentDate).HasColumnType("date");
        builder.Property(x => x.SupplierCodeSnapshot).HasMaxLength(50);
        builder.Property(x => x.SupplierNameSnapshot).HasMaxLength(200);
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false);
        builder.Property(x => x.LastSynchronizedAtUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.ExternalVersion).HasMaxLength(100);
        builder.Property(x => x.ExternalStatus).HasMaxLength(30);

        builder.HasOne(x => x.Header).WithMany(x => x.SourceDocuments)
            .HasForeignKey(x => x.GrHeaderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.GrHeaderId).HasDatabaseName("IX_RII_GR_SOURCE_DOCUMENT_HEADER");
        builder.HasIndex(x => new { x.GrHeaderId, x.SourceSystem, x.SourceDocumentType, x.ExternalDocumentNo })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_GR_SOURCE_DOCUMENT_EXTERNAL");
    }
}

public sealed class GoodsReceiptLineConfiguration : BaseEntityConfiguration<GoodsReceiptLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        builder.ToTable("RII_GR_LINE", table =>
        {
            table.HasCheckConstraint("CK_RII_GR_LINE_LINE_NO", "[LineNo] > 0");
            table.HasCheckConstraint("CK_RII_GR_LINE_UNIT_FACTOR", "[UnitConversionFactor] > 0");
            table.HasCheckConstraint("CK_RII_GR_LINE_QUANTITIES_NONNEGATIVE", "[ExpectedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [AcceptedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [QuarantineQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [ShortClosedQuantity] >= 0");
            table.HasCheckConstraint("CK_RII_GR_LINE_QUALITY_TOTAL", "[AcceptedQuantity] + [RejectedQuantity] + [QuarantineQuantity] <= [ReceivedQuantity]");
            table.HasCheckConstraint("CK_RII_GR_LINE_PUTAWAY_TOTAL", "[PutawayQuantity] <= [AcceptedQuantity]");
            table.HasCheckConstraint("CK_RII_GR_LINE_OVER_TOLERANCE", "[OverReceiptTolerancePercent] BETWEEN 0 AND 100");
        });

        builder.Property(x => x.StockCodeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(x => x.StockNameSnapshot).HasMaxLength(250);
        builder.Property(x => x.YapCodeSnapshot).HasMaxLength(50);
        builder.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.BaseUnitCode).HasMaxLength(20).IsRequired();
        Quantity(builder.Property(x => x.UnitConversionFactor), 8);
        Quantity(builder.Property(x => x.ExpectedQuantity));
        Quantity(builder.Property(x => x.ReceivedQuantity));
        Quantity(builder.Property(x => x.AcceptedQuantity));
        Quantity(builder.Property(x => x.RejectedQuantity));
        Quantity(builder.Property(x => x.QuarantineQuantity));
        Quantity(builder.Property(x => x.PutawayQuantity));
        Quantity(builder.Property(x => x.ShortClosedQuantity));
        builder.Property(x => x.TrackingType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.OverReceiptTolerancePercent).HasPrecision(9, 4);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Header).WithMany(x => x.Lines)
            .HasForeignKey(x => x.GrHeaderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Stock.Domain.Stock>().WithMany()
            .HasForeignKey(x => x.StockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.YapCode.Domain.YapCode>().WithMany()
            .HasForeignKey(x => x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany()
            .HasForeignKey(x => x.DefaultReceivingLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Warehouse.Domain.Warehouse>().WithMany()
            .HasForeignKey(x => x.TargetWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Modules.Location.Domain.WarehouseLocation>().WithMany()
            .HasForeignKey(x => x.DefaultPutawayLocationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.GrHeaderId, x.LineNo })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_GR_LINE_HEADER_LINE_NO");
        builder.HasIndex(x => new { x.TargetWarehouseId, x.Status, x.StockId })
            .HasDatabaseName("IX_RII_GR_LINE_TARGET_WAREHOUSE_STATUS_STOCK");
        builder.HasIndex(x => new { x.StockId, x.YapCodeId, x.Status })
            .HasDatabaseName("IX_RII_GR_LINE_STOCK_YAP_STATUS");
    }

    private static void Quantity(PropertyBuilder<decimal> property, int scale = 6) => property.HasPrecision(18, scale).IsRequired();
}

public sealed class GoodsReceiptLineSourceConfiguration : BaseEntityConfiguration<GoodsReceiptLineSource>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptLineSource> builder)
    {
        builder.ToTable("RII_GR_LINE_SOURCE", table =>
            table.HasCheckConstraint("CK_RII_GR_LINE_SOURCE_QUANTITIES", "[OrderedQuantity] >= 0 AND [PreviouslyReceivedQuantity] >= 0 AND [AllocatedQuantity] >= 0 AND [ReceivedQuantity] >= 0"));
        builder.Property(x => x.ExternalLineId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExternalStockCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExternalYapCode).HasMaxLength(50);
        Quantity(builder.Property(x => x.OrderedQuantity));
        Quantity(builder.Property(x => x.PreviouslyReceivedQuantity));
        Quantity(builder.Property(x => x.AllocatedQuantity));
        Quantity(builder.Property(x => x.ReceivedQuantity));
        builder.Property(x => x.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ExternalStatus).HasMaxLength(30);

        builder.HasOne(x => x.Line).WithMany(x => x.Sources)
            .HasForeignKey(x => x.GrLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceDocument).WithMany(x => x.LineSources)
            .HasForeignKey(x => x.GrSourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.GrLineId, x.GrSourceDocumentId, x.ExternalLineId })
            .IsUnique().HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_RII_GR_LINE_SOURCE_EXTERNAL_LINE");
        builder.HasIndex(x => x.GrSourceDocumentId).HasDatabaseName("IX_RII_GR_LINE_SOURCE_DOCUMENT");
    }

    private static void Quantity(PropertyBuilder<decimal> property) => property.HasPrecision(18, 6).IsRequired();
}

public sealed class GoodsReceiptStatusHistoryConfiguration : BaseEntityConfiguration<GoodsReceiptStatusHistory>
{
    protected override void ConfigureEntity(EntityTypeBuilder<GoodsReceiptStatusHistory> builder)
    {
        builder.ToTable("RII_GR_STATUS_HISTORY");
        builder.Property(x => x.StatusArea).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.FromStatus).HasMaxLength(30);
        builder.Property(x => x.ToStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ChangedAtUtc).HasColumnType("datetimeoffset(7)").IsRequired();
        builder.Property(x => x.ReasonCode).HasMaxLength(50);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasOne(x => x.Header).WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.GrHeaderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.GrHeaderId, x.ChangedAtUtc })
            .HasDatabaseName("IX_RII_GR_STATUS_HISTORY_HEADER_CHANGED_AT");
        builder.HasIndex(x => new { x.GrHeaderId, x.CorrelationId }).IsUnique()
            .HasDatabaseName("UX_RII_GR_STATUS_HISTORY_HEADER_CORRELATION_ID");
    }
}
