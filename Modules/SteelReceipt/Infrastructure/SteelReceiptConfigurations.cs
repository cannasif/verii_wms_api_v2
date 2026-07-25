using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.SteelReceipt.Infrastructure;

public sealed class SteelReceiptPlanConfiguration : BaseEntityConfiguration<SteelReceiptPlan>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SteelReceiptPlan> b)
    {
        b.ToTable("RII_STEEL_RECEIPT_PLAN", t => {
            t.HasCheckConstraint("CK_RII_STEEL_PLAN_LINE_COUNT", "[TotalLineCount] >= 0");
            t.HasCheckConstraint("CK_RII_STEEL_PLAN_QUANTITY", "[TotalExpectedQuantity] >= 0"); });
        b.Property(x=>x.ImportReferenceNo).HasMaxLength(100).IsRequired();
        b.Property(x=>x.SourceFileName).HasMaxLength(260).IsRequired();
        b.Property(x=>x.ExportReferenceNo).HasMaxLength(100);
        b.Property(x=>x.SupplierCodeSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x=>x.SupplierNameSnapshot).HasMaxLength(300).IsRequired();
        b.Property(x=>x.WaybillNo).HasMaxLength(50);
        b.Property(x=>x.Description).HasMaxLength(1000);
        b.Property(x=>x.Status).HasConversion<string>().HasMaxLength(40);
        b.Property(x=>x.TotalExpectedQuantity).HasPrecision(18,6);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>x.CorrelationId).IsUnique();
        b.HasIndex(x=>new{x.BranchCode,x.ImportReferenceNo}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.SupplierId,x.Status,x.PlannedArrivalAtUtc});
        b.HasIndex(x=>x.VehicleCheckInId);
        b.HasOne<verii_wms_api_v2.Modules.VehicleCheckIn.Domain.VehicleCheckInHeader>().WithMany().HasForeignKey(x=>x.VehicleCheckInId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.Customer.Domain.Customer>().WithMany().HasForeignKey(x=>x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x=>x.TargetWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x=>x.ReceivingLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.DocumentSeries.Domain.DocumentSeries>().WithMany().HasForeignKey(x=>x.DocumentSeriesId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x=>x.Lines).WithOne(x=>x.Plan).HasForeignKey(x=>x.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SteelReceiptPlanLineConfiguration : BaseEntityConfiguration<SteelReceiptPlanLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SteelReceiptPlanLine> b)
    {
        b.ToTable("RII_STEEL_RECEIPT_PLAN_LINE", t => {
            t.HasCheckConstraint("CK_RII_STEEL_LINE_NO", "[LineNo] > 0");
            t.HasCheckConstraint("CK_RII_STEEL_LINE_QTY", "[ExpectedQuantity] > 0 AND [ArrivedQuantity] >= 0 AND [ApprovedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [ApprovedQuantity] + [RejectedQuantity] <= [ArrivedQuantity] AND [ArrivedQuantity] <= [ExpectedQuantity]"); });
        b.Property(x=>x.DCode).HasMaxLength(60).IsRequired();
        b.Property(x=>x.ExternalLineKey).HasMaxLength(450).IsRequired();
        b.Property(x=>x.NetsisOrderNo).HasMaxLength(50); b.Property(x=>x.NetsisOrderLineNo).HasMaxLength(50);
        b.Property(x=>x.StockCodeSnapshot).HasMaxLength(100).IsRequired(); b.Property(x=>x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x=>x.YapCodeSnapshot).HasMaxLength(100); b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x=>x.SupplierSerialNo).HasMaxLength(100).IsRequired(); b.Property(x=>x.SecondarySerialNo).HasMaxLength(100);
        b.Property(x=>x.CombinedSize).HasMaxLength(100); b.Property(x=>x.MaterialGrade).HasMaxLength(100);
        b.Property(x=>x.HeatNumber).HasMaxLength(100); b.Property(x=>x.CertificateNumber).HasMaxLength(100);
        b.Property(x=>x.RejectReason).HasMaxLength(500); b.Property(x=>x.InspectionNote).HasMaxLength(1000);
        foreach(var p in new[]{nameof(SteelReceiptPlanLine.ExpectedQuantity),nameof(SteelReceiptPlanLine.ArrivedQuantity),nameof(SteelReceiptPlanLine.ApprovedQuantity),nameof(SteelReceiptPlanLine.RejectedQuantity)}) b.Property(p).HasPrecision(18,6);
        b.Property(x=>x.ArrivalStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.InspectionStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.ConversionStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.PutawayStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>new{x.PlanId,x.LineNo}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>x.DCode).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.PlanId,x.ExternalLineKey}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.StockId,x.SupplierSerialNo}); b.HasIndex(x=>new{x.InspectionStatus,x.ConversionStatus});
        b.HasOne<verii_wms_api_v2.Modules.Stock.Domain.Stock>().WithMany().HasForeignKey(x=>x.StockId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.YapCode.Domain.YapCode>().WithMany().HasForeignKey(x=>x.YapCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x=>x.TargetWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x=>x.ReceivingLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.GoodsReceipt.Domain.GoodsReceiptHeader>().WithMany().HasForeignKey(x=>x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.GoodsReceipt.Domain.GoodsReceiptLine>().WithMany().HasForeignKey(x=>x.GoodsReceiptLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x=>x.Attachments).WithOne(x=>x.PlanLine).HasForeignKey(x=>x.PlanLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.Placement).WithOne(x=>x.PlanLine).HasForeignKey<SteelReceiptPlacement>(x=>x.PlanLineId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SteelReceiptInspectionAttachmentConfiguration : BaseEntityConfiguration<SteelReceiptInspectionAttachment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SteelReceiptInspectionAttachment> b)
    {
        b.ToTable("RII_STEEL_RECEIPT_ATTACHMENT");
        b.Property(x=>x.FileName).HasMaxLength(260).IsRequired(); b.Property(x=>x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x=>x.StoragePath).HasMaxLength(500).IsRequired(); b.Property(x=>x.Caption).HasMaxLength(500);
        b.HasIndex(x=>new{x.PlanLineId,x.CreatedDate});
    }
}

public sealed class SteelReceiptPlacementConfiguration : BaseEntityConfiguration<SteelReceiptPlacement>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SteelReceiptPlacement> b)
    {
        b.ToTable("RII_STEEL_RECEIPT_PLACEMENT", t => {
            t.HasCheckConstraint("CK_RII_STEEL_PLACEMENT_COORDINATES", "[RowNo] > 0 AND [PositionNo] > 0");
            t.HasCheckConstraint("CK_RII_STEEL_PLACEMENT_STACK", "[PlacementType] <> 'Stacked' OR [StackOrderNo] > 0");
        });
        b.Property(x=>x.PlacementType).HasConversion<string>().HasMaxLength(30); b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>x.PlanLineId).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x=>new{x.WarehouseId,x.LocationId,x.RowNo,x.PositionNo,x.StackOrderNo}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne<verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse>().WithMany().HasForeignKey(x=>x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.Location.Domain.WarehouseLocation>().WithMany().HasForeignKey(x=>x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<verii_wms_api_v2.Modules.StockMovement.Domain.StockMovementOperation>().WithMany().HasForeignKey(x=>x.StockMovementOperationId).OnDelete(DeleteBehavior.Restrict);
    }
}
