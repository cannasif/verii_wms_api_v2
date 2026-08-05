using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Infrastructure;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Infrastructure;

public sealed class WarehouseTransferHeaderConfiguration : BaseEntityConfiguration<WarehouseTransferHeader>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferHeader> b)
    {
        b.ToTable("RII_WT_HEADER");
        b.Property(x=>x.DocumentNo).HasMaxLength(50).IsRequired();
        b.Property(x=>x.ExternalReferenceNo).HasMaxLength(100);
        b.Property(x=>x.ProjectCode).HasMaxLength(50);
        b.Property(x=>x.ShipmentNo).HasMaxLength(50); b.Property(x=>x.WaybillNo).HasMaxLength(50);
        b.Property(x=>x.CarrierCode).HasMaxLength(50); b.Property(x=>x.CarrierName).HasMaxLength(200);
        b.Property(x=>x.VehiclePlate).HasMaxLength(20); b.Property(x=>x.TrailerPlate).HasMaxLength(20);
        b.Property(x=>x.DriverName).HasMaxLength(200); b.Property(x=>x.SealNo).HasMaxLength(50);
        b.Property(x=>x.CancellationReason).HasMaxLength(1000); b.Property(x=>x.Description).HasMaxLength(2000);
        b.Property(x=>x.ReservationPolicy).HasConversion<string>().HasMaxLength(30).HasDefaultValue(WarehouseTransferReservationPolicy.OnRelease).HasSentinel((WarehouseTransferReservationPolicy)0);
        b.Property(x=>x.BusinessContext).HasConversion<string>().HasMaxLength(40).HasDefaultValue(WarehouseTransferBusinessContext.InterWarehouse).HasSentinel((WarehouseTransferBusinessContext)0);
        b.Property(x=>x.DirectPostingPolicy).HasConversion<string>().HasMaxLength(30).HasDefaultValue(WarehouseTransferDirectPostingPolicy.TwoStepTransit).HasSentinel((WarehouseTransferDirectPostingPolicy)0);
        b.Property(x=>x.DiscrepancyPolicy).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.CancellationReturnPolicy).HasConversion<string>().HasMaxLength(40)
            .HasDefaultValue(WarehouseTransferCancellationReturnPolicy.OriginalSourceLocation)
            .HasSentinel((WarehouseTransferCancellationReturnPolicy)0);
        b.Property(x=>x.MinimumFulfillmentPercent).HasPrecision(9,4).HasDefaultValue(100m);
        b.Property(x=>x.RequireAssignee).HasDefaultValue(true);
        b.Property(x=>x.RequireSourceLocation).HasDefaultValue(true);
        b.Property(x=>x.RequireTargetLocation).HasDefaultValue(true);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>new{x.BranchCode,x.DocumentNo}).IsUnique();
        b.HasIndex(x=>x.CorrelationId).IsUnique();
        b.HasIndex(x=>new{x.BranchCode,x.Status,x.PlannedDispatchAtUtc});
    }
}

public sealed class WarehouseTransferSourceDocumentConfiguration : BaseEntityConfiguration<WarehouseTransferSourceDocument>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferSourceDocument> b)
    {
        b.ToTable("RII_WT_SOURCE_DOCUMENT");
        b.Property(x=>x.SourceDocumentType).HasMaxLength(50).IsRequired();
        b.Property(x=>x.ExternalDocumentNo).HasMaxLength(100).IsRequired();
        b.Property(x=>x.ExternalDocumentId).HasMaxLength(100); b.Property(x=>x.ExternalStatus).HasMaxLength(50);
        b.HasOne(x=>x.Header).WithMany(x=>x.SourceDocuments).HasForeignKey(x=>x.WtHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.WtHeaderId,x.SourceDocumentType,x.ExternalDocumentNo}).IsUnique();
    }
}

public sealed class WarehouseTransferLineConfiguration : BaseEntityConfiguration<WarehouseTransferLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferLine> b)
    {
        b.ToTable("RII_WT_LINE",t=>t.HasCheckConstraint("CK_RII_WT_LINE_QTY","[RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [DamagedQuantity] >= 0 AND [LostQuantity] >= 0 AND [ShortClosedQuantity] >= 0"));
        b.Property(x=>x.StockCodeSnapshot).HasMaxLength(100).IsRequired(); b.Property(x=>x.StockNameSnapshot).HasMaxLength(300);
        b.Property(x=>x.YapCodeSnapshot).HasMaxLength(100); b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired();
        b.Property(x=>x.BaseUnitCode).HasMaxLength(20).IsRequired(); b.Property(x=>x.UnitConversionFactor).HasPrecision(20,8);
        b.Property(x=>x.SourceStockStatus).HasMaxLength(40).IsRequired(); b.Property(x=>x.TargetStockStatus).HasMaxLength(40).IsRequired();
        foreach(var p in new[]{nameof(WarehouseTransferLine.RequestedQuantity),nameof(WarehouseTransferLine.ReservedQuantity),nameof(WarehouseTransferLine.PickedQuantity),nameof(WarehouseTransferLine.PackedQuantity),nameof(WarehouseTransferLine.ShippedQuantity),nameof(WarehouseTransferLine.ReceivedQuantity),nameof(WarehouseTransferLine.PutawayQuantity),nameof(WarehouseTransferLine.DamagedQuantity),nameof(WarehouseTransferLine.LostQuantity),nameof(WarehouseTransferLine.ShortClosedQuantity)}) b.Property(p).HasPrecision(20,6);
        b.Property(x=>x.Description).HasMaxLength(1000); b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.Header).WithMany(x=>x.Lines).HasForeignKey(x=>x.WtHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.WtHeaderId,x.LineNo}).IsUnique(); b.HasIndex(x=>new{x.StockId,x.Status});
    }
}

public sealed class WarehouseTransferLineSourceConfiguration : BaseEntityConfiguration<WarehouseTransferLineSource>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferLineSource> b)
    {
        b.ToTable("RII_WT_LINE_SOURCE");
        b.Property(x=>x.ExternalLineId).HasMaxLength(100).IsRequired(); b.Property(x=>x.ExternalStockCode).HasMaxLength(100).IsRequired();
        b.Property(x=>x.ExternalYapCode).HasMaxLength(100); b.Property(x=>x.UnitCode).HasMaxLength(20).IsRequired(); b.Property(x=>x.ExternalStatus).HasMaxLength(50);
        b.Property(x=>x.OrderedQuantity).HasPrecision(20,6); b.Property(x=>x.PreviouslyTransferredQuantity).HasPrecision(20,6); b.Property(x=>x.AllocatedQuantity).HasPrecision(20,6);
        b.HasOne(x=>x.Line).WithMany(x=>x.Sources).HasForeignKey(x=>x.WtLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.SourceDocument).WithMany(x=>x.LineSources).HasForeignKey(x=>x.WtSourceDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.WtLineId,x.WtSourceDocumentId,x.ExternalLineId}).IsUnique();
        b.HasIndex(x=>new{x.WtSourceDocumentId,x.ExternalLineId});
    }
}

public sealed class WarehouseTransferTrackingConfiguration : BaseEntityConfiguration<WarehouseTransferTracking>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferTracking> b)
    {
        b.ToTable("RII_WT_TRACKING",t=>t.HasCheckConstraint("CK_RII_WT_TRACKING_QTY","[PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0"));
        b.Property(x=>x.HandlingUnitNo).HasMaxLength(100); b.Property(x=>x.LotNo).HasMaxLength(100); b.Property(x=>x.SerialNo).HasMaxLength(200);
        foreach(var p in new[]{nameof(WarehouseTransferTracking.PlannedQuantity),nameof(WarehouseTransferTracking.ReservedQuantity),nameof(WarehouseTransferTracking.PickedQuantity),nameof(WarehouseTransferTracking.PackedQuantity),nameof(WarehouseTransferTracking.ShippedQuantity),nameof(WarehouseTransferTracking.ReceivedQuantity),nameof(WarehouseTransferTracking.PutawayQuantity)}) b.Property(p).HasPrecision(20,6);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.Line).WithMany(x=>x.Trackings).HasForeignKey(x=>x.WtLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.WtLineId,x.SerialNo}); b.HasIndex(x=>new{x.LotNo,x.SerialNo,x.Status});
    }
}

public sealed class WarehouseTransferTaskConfiguration : BaseEntityConfiguration<WarehouseTransferTask>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferTask> b)
    {
        b.ToTable("RII_WT_TASK"); b.Property(x=>x.TaskNo).HasMaxLength(50).IsRequired(); b.Property(x=>x.Description).HasMaxLength(1000); b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.Header).WithMany(x=>x.Tasks).HasForeignKey(x=>x.WtHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseTransferTask>().WithMany().HasForeignKey(x=>x.OriginTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseTransferTask>().WithMany().HasForeignKey(x=>x.PreviousTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.BranchCode,x.TaskNo}).IsUnique(); b.HasIndex(x=>new{x.WarehouseId,x.TaskType,x.Status}); b.HasIndex(x=>x.OriginTaskId); b.HasIndex(x=>x.PreviousTaskId);
    }
}

public sealed class WarehouseTransferTaskLineConfiguration : BaseEntityConfiguration<WarehouseTransferTaskLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferTaskLine> b)
    {
        b.ToTable("RII_WT_TASK_LINE",t=>t.HasCheckConstraint("CK_RII_WT_TASK_LINE_QTY","[PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0")); b.Property(x=>x.PlannedQuantity).HasPrecision(20,6); b.Property(x=>x.ProcessedQuantity).HasPrecision(20,6); b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasOne(x=>x.Task).WithMany(x=>x.Lines).HasForeignKey(x=>x.WtTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.Line).WithMany(x=>x.TaskLines).HasForeignKey(x=>x.WtLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.WtTaskId,x.WtLineId}).IsUnique();
    }
}

public sealed class WarehouseTransferTaskAssignmentConfiguration : BaseEntityConfiguration<WarehouseTransferTaskAssignment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferTaskAssignment> b)
    {
        b.ToTable("RII_WT_TASK_ASSIGNMENT");
        b.HasOne(x=>x.Task).WithMany(x=>x.Assignments).HasForeignKey(x=>x.WtTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.WtTaskId,x.UserId}).IsUnique(); b.HasIndex(x=>new{x.UserId,x.AcceptedAtUtc});
    }
}

public sealed class WarehouseTransferStatusHistoryConfiguration : BaseEntityConfiguration<WarehouseTransferStatusHistory>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferStatusHistory> b)
    {
        b.ToTable("RII_WT_STATUS_HISTORY"); b.Property(x=>x.FromStatus).HasMaxLength(50); b.Property(x=>x.ToStatus).HasMaxLength(50).IsRequired();
        b.Property(x=>x.ReasonCode).HasMaxLength(100); b.Property(x=>x.Description).HasMaxLength(1000);
        b.HasOne(x=>x.Header).WithMany(x=>x.StatusHistory).HasForeignKey(x=>x.WtHeaderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.WtHeaderId,x.ChangedAtUtc}); b.HasIndex(x=>x.CorrelationId);
    }
}

public sealed class WarehouseTransferPolicyConfiguration : BaseEntityConfiguration<WarehouseTransferPolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WarehouseTransferPolicy> b)
    {
        b.ToTable("RII_WT_POLICIES",t=>t.HasCheckConstraint("CK_RII_WT_POLICY_FULFILLMENT","[MinimumFulfillmentPercent] >= 0 AND [MinimumFulfillmentPercent] <= 100"));
        b.Property(x=>x.PolicyKey).HasMaxLength(30).IsRequired();
        b.Property(x=>x.ReservationPolicy).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.DirectPostingPolicy).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.DiscrepancyPolicy).HasConversion<string>().HasMaxLength(30);
        b.Property(x=>x.CancellationReturnPolicy).HasConversion<string>().HasMaxLength(40)
            .HasDefaultValue(WarehouseTransferCancellationReturnPolicy.OriginalSourceLocation)
            .HasSentinel((WarehouseTransferCancellationReturnPolicy)0);
        b.Property(x=>x.MinimumFulfillmentPercent).HasPrecision(9,4);
        b.Property(x=>x.RowVersion).IsRowVersion();
        b.HasIndex(x=>new{x.BranchCode,x.PolicyKey}).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
