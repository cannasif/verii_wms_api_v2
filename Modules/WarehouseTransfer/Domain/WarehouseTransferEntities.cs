using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Domain;

public enum WarehouseTransferInitiationMode
{
    OrderBasedTask = 1,
    StockBasedTask = 2,
    DirectTransfer = 3,
    OrderBasedDirectTransfer = 4
}

public enum WarehouseTransferReservationPolicy { None = 1, OnCreate = 2, OnRelease = 3 }
public enum WarehouseTransferDirectPostingPolicy { OneStep = 1, TwoStepTransit = 2 }

public enum WarehouseTransferBusinessContext
{
    InterWarehouse = 1,
    ProductionMaterialSupply = 2,
    ProductionWipMove = 3,
    ProductionOutputMove = 4,
    SubcontractingIssue = 5,
    SubcontractingReceipt = 6,
    SubcontractorToSubcontractor = 7
}

public enum WarehouseTransferProcessType
{
    ErpOrderBased = 1,
    InternalRequest = 2,
    Replenishment = 3,
    ReturnToWarehouse = 4,
    Direct = 5
}

public enum WarehouseTransferStatus
{
    Draft = 1,
    Released = 2,
    Picking = 3,
    PartiallyPicked = 4,
    Picked = 5,
    Shipped = 6,
    PartiallyReceived = 7,
    Received = 8,
    PartiallyPutaway = 9,
    Completed = 10,
    Cancelled = 11
}

public enum WarehouseTransferLineStatus
{
    Open = 1,
    Reserved = 2,
    PartiallyPicked = 3,
    Picked = 4,
    Shipped = 5,
    PartiallyReceived = 6,
    Received = 7,
    Putaway = 8,
    ShortClosed = 9,
    Cancelled = 10
}

public enum WarehouseTransferTaskType
{
    Pick = 1,
    Dispatch = 2,
    Receive = 3,
    Putaway = 4
}

public enum WarehouseTransferTaskStatus
{
    Open = 1,
    Assigned = 2,
    Accepted = 3,
    InProgress = 4,
    PartiallyCompleted = 5,
    Completed = 6,
    Cancelled = 7
}

public enum WarehouseTransferTrackingStatus
{
    Planned = 1,
    Reserved = 2,
    Picked = 3,
    Shipped = 4,
    Received = 5,
    Putaway = 6,
    Damaged = 7,
    Lost = 8,
    Cancelled = 9
}

public enum WarehouseTransferDiscrepancyPolicy
{
    Block = 1,
    AllowWithReason = 2,
    RequireApproval = 3
}

public enum WarehouseTransferStatusArea
{
    Operation = 1,
    Approval = 2,
    Picking = 3,
    Dispatch = 4,
    Transit = 5,
    Receiving = 6,
    Putaway = 7,
    ErpIntegration = 8
}

public sealed class WarehouseTransferHeader : BaseEntity
{
    public long DocumentSeriesId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public WarehouseTransferBusinessContext BusinessContext { get; set; } = WarehouseTransferBusinessContext.InterWarehouse;
    public WarehouseTransferInitiationMode InitiationMode { get; set; }
    public WarehouseTransferProcessType ProcessType { get; set; }
    public WarehouseOperationSourceSystem SourceSystem { get; set; } = WarehouseOperationSourceSystem.Manual;
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public string? ExternalReferenceNo { get; set; }

    public long SourceWarehouseId { get; set; }
    public long TargetWarehouseId { get; set; }
    public long? SourceStagingLocationId { get; set; }
    public long? TargetReceivingLocationId { get; set; }
    public long? TargetPutawayLocationId { get; set; }

    public WarehouseTransferStatus Status { get; set; } = WarehouseTransferStatus.Draft;
    public OperationApprovalStatus ApprovalStatus { get; set; } = OperationApprovalStatus.NotRequired;
    public ErpIntegrationStatus ErpIntegrationStatus { get; set; } = ErpIntegrationStatus.Pending;

    public DateTimeOffset? PlannedDispatchAtUtc { get; set; }
    public DateTimeOffset? PlannedArrivalAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public long? ReleasedBy { get; set; }
    public DateTimeOffset? ShippedAtUtc { get; set; }
    public long? ShippedBy { get; set; }
    public DateTimeOffset? ReceivedAtUtc { get; set; }
    public long? ReceivedBy { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public long? CompletedBy { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public long? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }

    public string? ShipmentNo { get; set; }
    public string? WaybillNo { get; set; }
    public DateOnly? WaybillDate { get; set; }
    public string? CarrierCode { get; set; }
    public string? CarrierName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? DriverName { get; set; }
    public string? SealNo { get; set; }

    // Süreç parametreleri başlığa snapshot alınır; devam eden emir sonradan değişen ayarlardan etkilenmez.
    public bool RequireApproval { get; set; }
    public bool AllowPartialPicking { get; set; } = true;
    public bool AllowPartialShipment { get; set; } = true;
    public bool AllowPartialReceipt { get; set; } = true;
    public bool RequireDestinationAcceptance { get; set; } = true;
    public bool RequirePutaway { get; set; } = true;
    public bool CreateTransitInventory { get; set; } = true;
    public WarehouseTransferDiscrepancyPolicy DiscrepancyPolicy { get; set; } = WarehouseTransferDiscrepancyPolicy.RequireApproval;
    public WarehouseTransferReservationPolicy ReservationPolicy { get; set; } = WarehouseTransferReservationPolicy.OnRelease;
    public WarehouseTransferDirectPostingPolicy DirectPostingPolicy { get; set; } = WarehouseTransferDirectPostingPolicy.TwoStepTransit;
    public bool RequireAssignee { get; set; } = true;
    public bool RequireSourceLocation { get; set; } = true;
    public bool RequireTargetLocation { get; set; } = true;
    public bool RequireShipmentInformation { get; set; }
    public bool AutoRelease { get; set; }
    public decimal MinimumFulfillmentPercent { get; set; } = 100m;

    public byte Priority { get; set; } = 3;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<WarehouseTransferSourceDocument> SourceDocuments { get; set; } = [];
    public ICollection<WarehouseTransferLine> Lines { get; set; } = [];
    public ICollection<WarehouseTransferTask> Tasks { get; set; } = [];
    public ICollection<WarehouseTransferStatusHistory> StatusHistory { get; set; } = [];
}

public sealed class WarehouseTransferSourceDocument : BaseEntity
{
    public long WtHeaderId { get; set; }
    public WarehouseTransferHeader Header { get; set; } = null!;
    public WarehouseOperationSourceSystem SourceSystem { get; set; } = WarehouseOperationSourceSystem.Netsis;
    public string SourceDocumentType { get; set; } = string.Empty;
    public string ExternalDocumentNo { get; set; } = string.Empty;
    public DateOnly? ExternalDocumentDate { get; set; }
    public string? ExternalDocumentId { get; set; }
    public string? ExternalStatus { get; set; }
    public DateTimeOffset? LastSynchronizedAtUtc { get; set; }
    public ICollection<WarehouseTransferLineSource> LineSources { get; set; } = [];
}

public sealed class WarehouseTransferLine : BaseEntity
{
    public long WtHeaderId { get; set; }
    public WarehouseTransferHeader Header { get; set; } = null!;
    public int LineNo { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public decimal UnitConversionFactor { get; set; } = 1m;

    public decimal RequestedQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal PackedQuantity { get; set; }
    public decimal ShippedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PutawayQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal LostQuantity { get; set; }
    public decimal ShortClosedQuantity { get; set; }

    public StockTrackingType TrackingType { get; set; } = StockTrackingType.None;
    public bool RequireLot { get; set; }
    public bool RequireSerial { get; set; }
    public bool RequireHandlingUnit { get; set; }
    public long SourceWarehouseId { get; set; }
    public long TargetWarehouseId { get; set; }
    public long? DefaultSourceLocationId { get; set; }
    public long? DefaultTargetLocationId { get; set; }
    public string SourceStockStatus { get; set; } = "Available";
    public string TargetStockStatus { get; set; } = "Available";
    public WarehouseTransferLineStatus Status { get; set; } = WarehouseTransferLineStatus.Open;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<WarehouseTransferLineSource> Sources { get; set; } = [];
    public ICollection<WarehouseTransferTracking> Trackings { get; set; } = [];
    public ICollection<WarehouseTransferTaskLine> TaskLines { get; set; } = [];
}

public sealed class WarehouseTransferLineSource : BaseEntity
{
    public long WtLineId { get; set; }
    public WarehouseTransferLine Line { get; set; } = null!;
    public long WtSourceDocumentId { get; set; }
    public WarehouseTransferSourceDocument SourceDocument { get; set; } = null!;
    public string ExternalLineId { get; set; } = string.Empty;
    public int? ExternalLineNo { get; set; }
    public string ExternalStockCode { get; set; } = string.Empty;
    public string? ExternalYapCode { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal PreviouslyTransferredQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string? ExternalStatus { get; set; }
}

public sealed class WarehouseTransferTracking : BaseEntity
{
    public long WtLineId { get; set; }
    public WarehouseTransferLine Line { get; set; } = null!;
    public string? HandlingUnitNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal PackedQuantity { get; set; }
    public decimal ShippedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal PutawayQuantity { get; set; }
    public long? SourceLocationId { get; set; }
    public long? TargetLocationId { get; set; }
    public WarehouseTransferTrackingStatus Status { get; set; } = WarehouseTransferTrackingStatus.Planned;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class WarehouseTransferTask : BaseEntity
{
    public long WtHeaderId { get; set; }
    public WarehouseTransferHeader Header { get; set; } = null!;
    public string TaskNo { get; set; } = string.Empty;
    public WarehouseTransferTaskType TaskType { get; set; }
    public long WarehouseId { get; set; }
    public WarehouseTransferTaskStatus Status { get; set; } = WarehouseTransferTaskStatus.Open;
    public byte Priority { get; set; } = 3;
    public DateTimeOffset? PlannedAtUtc { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public long? AcceptedBy { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public long? StartedBy { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public long? CompletedBy { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<WarehouseTransferTaskLine> Lines { get; set; } = [];
    public ICollection<WarehouseTransferTaskAssignment> Assignments { get; set; } = [];
}

public sealed class WarehouseTransferTaskLine : BaseEntity
{
    public long WtTaskId { get; set; }
    public WarehouseTransferTask Task { get; set; } = null!;
    public long WtLineId { get; set; }
    public WarehouseTransferLine Line { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal ProcessedQuantity { get; set; }
    public long? SourceLocationId { get; set; }
    public long? TargetLocationId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class WarehouseTransferTaskAssignment : BaseEntity
{
    public long WtTaskId { get; set; }
    public WarehouseTransferTask Task { get; set; } = null!;
    public long UserId { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; }
    public long AssignedBy { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
}

public sealed class WarehouseTransferStatusHistory : BaseEntity
{
    public long WtHeaderId { get; set; }
    public WarehouseTransferHeader Header { get; set; } = null!;
    public WarehouseTransferStatusArea StatusArea { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; set; }
    public long? ChangedBy { get; set; }
    public string? ReasonCode { get; set; }
    public string? Description { get; set; }
    public Guid CorrelationId { get; set; }
}

public sealed class WarehouseTransferPolicy : BaseEntity
{
    public string PolicyKey { get; set; } = "DEFAULT";
    public bool AllowOrderBasedTask { get; set; } = true;
    public bool AllowStockBasedTask { get; set; } = true;
    public bool AllowOrderBasedDirect { get; set; }
    public bool AllowStockBasedDirect { get; set; } = true;
    public bool RequireApproval { get; set; }
    public bool RequireAssigneeForTask { get; set; } = true;
    public bool AllowMultipleAssignees { get; set; } = true;
    public bool AutoReleaseTaskBased { get; set; }
    public WarehouseTransferReservationPolicy ReservationPolicy { get; set; } = WarehouseTransferReservationPolicy.OnRelease;
    public decimal MinimumFulfillmentPercent { get; set; } = 100m;
    public bool AllowPartialPicking { get; set; } = true;
    public bool AllowPartialShipment { get; set; } = true;
    public bool AllowPartialReceipt { get; set; } = true;
    public bool RequireDestinationAcceptance { get; set; } = true;
    public bool CreateTransitInventory { get; set; } = true;
    public bool RequirePutaway { get; set; } = true;
    public bool RequireSourceLocation { get; set; } = true;
    public bool RequireTargetLocation { get; set; } = true;
    public bool RequireShipmentInformation { get; set; }
    public WarehouseTransferDirectPostingPolicy DirectPostingPolicy { get; set; } = WarehouseTransferDirectPostingPolicy.TwoStepTransit;
    public WarehouseTransferDiscrepancyPolicy DiscrepancyPolicy { get; set; } = WarehouseTransferDiscrepancyPolicy.RequireApproval;
    public byte[] RowVersion { get; set; } = [];
}
