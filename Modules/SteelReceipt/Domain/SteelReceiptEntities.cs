using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.SteelReceipt.Domain;

public enum SteelReceiptPlanStatus { Imported=1, InspectionInProgress=2, ReadyForReceipt=3, PartiallyConverted=4, Converted=5, Cancelled=6, PartiallyReadyForReceipt=7 }
public enum SteelArrivalStatus { Expected=1, Arrived=2, Missing=3 }
public enum SteelInspectionStatus { Pending=1, Inspected=2, Approved=3, PartiallyApproved=4, Rejected=5 }
public enum SteelReceiptConversionStatus { NotCreated=1, Created=2 }
public enum SteelReceiptConversionMode { Task=1, Direct=2 }
public enum SteelPutawayStatus { Pending=1, Placed=2 }
public enum SteelPlacementType { SideBySide=1, Stacked=2 }
public enum SteelVehicleAcceptanceStatus { Completed=1, Cancelled=2, PartiallyIdentified=3 }
public enum SteelPlateIdentityStatus { Known=1, Unknown=2, Resolved=3 }

public sealed class SteelReceiptPlan : BaseEntity
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public string ImportReferenceNo { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public string? ExportReferenceNo { get; set; }
    public long? VehicleCheckInId { get; set; }
    public long SupplierId { get; set; }
    public string SupplierCodeSnapshot { get; set; } = string.Empty;
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public long TargetWarehouseId { get; set; }
    public long ReceivingLocationId { get; set; }
    public long DocumentSeriesId { get; set; }
    public string? WaybillNo { get; set; }
    public DateOnly? WaybillDate { get; set; }
    public DateTimeOffset? PlannedArrivalAtUtc { get; set; }
    public SteelReceiptPlanStatus Status { get; set; } = SteelReceiptPlanStatus.Imported;
    public int TotalLineCount { get; set; }
    public decimal TotalExpectedQuantity { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public long ImportedBy { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<SteelReceiptPlanLine> Lines { get; set; } = [];
}

public sealed class SteelReceiptPlanLine : BaseEntity
{
    public long PlanId { get; set; }
    public SteelReceiptPlan Plan { get; set; } = null!;
    public int LineNo { get; set; }
    public string DCode { get; set; } = string.Empty;
    public string ExternalLineKey { get; set; } = string.Empty;
    public string? NetsisOrderNo { get; set; }
    public string? NetsisOrderLineNo { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public string SupplierSerialNo { get; set; } = string.Empty;
    public string? SecondarySerialNo { get; set; }
    public string? CombinedSize { get; set; }
    public string? MaterialGrade { get; set; }
    public string? HeatNumber { get; set; }
    public string? CertificateNumber { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal ArrivedQuantity { get; set; }
    public decimal ApprovedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public long TargetWarehouseId { get; set; }
    public long ReceivingLocationId { get; set; }
    public SteelArrivalStatus ArrivalStatus { get; set; } = SteelArrivalStatus.Expected;
    public SteelInspectionStatus InspectionStatus { get; set; } = SteelInspectionStatus.Pending;
    public SteelReceiptConversionStatus ConversionStatus { get; set; } = SteelReceiptConversionStatus.NotCreated;
    public SteelPutawayStatus PutawayStatus { get; set; } = SteelPutawayStatus.Pending;
    public string? RejectReason { get; set; }
    public string? InspectionNote { get; set; }
    public long? InspectedBy { get; set; }
    public DateTimeOffset? InspectedAtUtc { get; set; }
    public long? GoodsReceiptId { get; set; }
    public long? GoodsReceiptLineId { get; set; }
    public long? VehicleAcceptanceId { get; set; }
    public SteelVehicleAcceptance? VehicleAcceptance { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<SteelReceiptInspectionAttachment> Attachments { get; set; } = [];
    public SteelReceiptPlacement? Placement { get; set; }
}

public sealed class SteelVehicleAcceptance : BaseEntity
{
    public Guid IdempotencyKey { get; set; }
    public long VehicleCheckInId { get; set; }
    public int PlateCount { get; set; }
    public decimal TotalAcceptedQuantity { get; set; }
    public SteelVehicleAcceptanceStatus Status { get; set; } = SteelVehicleAcceptanceStatus.Completed;
    public DateTimeOffset AcceptedAtUtc { get; set; }
    public long AcceptedBy { get; set; }
    public string? Note { get; set; }
    public ICollection<SteelReceiptPlanLine> Lines { get; set; } = [];
    public ICollection<SteelVehicleAcceptedPlate> AcceptedPlates { get; set; } = [];
}

public sealed class SteelVehicleAcceptedPlate : BaseEntity
{
    public long VehicleCheckInId { get; set; }
    public long VehicleAcceptanceId { get; set; }
    public SteelVehicleAcceptance VehicleAcceptance { get; set; } = null!;
    public int SequenceNo { get; set; }
    public SteelPlateIdentityStatus IdentityStatus { get; set; } = SteelPlateIdentityStatus.Known;
    public long? PlanLineId { get; set; }
    public SteelReceiptPlanLine? PlanLine { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public long? ResolvedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SteelReceiptInspectionAttachment : BaseEntity
{
    public long PlanLineId { get; set; }
    public SteelReceiptPlanLine PlanLine { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public long FileSize { get; set; }
}

public sealed class SteelReceiptPlacement : BaseEntity
{
    public long PlanLineId { get; set; }
    public SteelReceiptPlanLine PlanLine { get; set; } = null!;
    public long WarehouseId { get; set; }
    public long LocationId { get; set; }
    public SteelPlacementType PlacementType { get; set; } = SteelPlacementType.Stacked;
    public int? RowNo { get; set; }
    public int? PositionNo { get; set; }
    public int? StackOrderNo { get; set; }
    public long StockMovementOperationId { get; set; }
    public DateTimeOffset PlacedAtUtc { get; set; }
    public long PlacedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
