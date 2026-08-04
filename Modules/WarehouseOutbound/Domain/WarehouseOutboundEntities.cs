using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.WarehouseOutbound.Domain;

public enum WarehouseOutboundInitiationMode { OrderBasedTask=1, StockBasedTask=2, StockBasedDirect=3, OrderBasedDirect=4 }
public enum WarehouseOutboundStatus { Draft=1, Released=2, Picking=3, Picked=4, Packing=5, Packed=6, Loading=7, Loaded=8, AwaitingApproval=9, Shipped=10, Cancelled=11 }
public enum WarehouseOutboundLineStatus { Open=1, Reserved=2, Picking=3, Picked=4, Packed=5, Loaded=6, Shipped=7, ShortClosed=8, Cancelled=9 }
public enum WarehouseOutboundTaskStatus { Open=1, Assigned=2, InProgress=3, Completed=4, Cancelled=5 }
public enum WarehouseOutboundTaskType { Pick=1, Pack=2, Load=3 }
public enum WarehouseOutboundReservationPolicy { None=1, OnCreate=2, OnRelease=3 }
public enum WarehouseOutboundPackingPolicy { NotRequired=1, Optional=2, Required=3 }
public enum WarehouseOutboundShortagePolicy { Block=1, AllowPartial=2, RequireApproval=3 }
public enum WarehouseOutboundOverPickPolicy { Block=1, AllowWithinTolerance=2, RequireApproval=3 }

public sealed class WarehouseOutboundHeader : BaseEntity
{
    public long DocumentSeriesId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public WarehouseOutboundInitiationMode InitiationMode { get; set; }
    public WarehouseOperationSourceSystem SourceSystem { get; set; }
    public Guid CorrelationId { get; set; }
    public long CustomerId { get; set; }
    public string CustomerCodeSnapshot { get; set; } = string.Empty;
    public string? CustomerNameSnapshot { get; set; }
    public long SourceWarehouseId { get; set; }
    public long? StagingLocationId { get; set; }
    public long? LoadingLocationId { get; set; }
    public WarehouseOutboundStatus Status { get; set; } = WarehouseOutboundStatus.Draft;
    public OperationApprovalStatus ApprovalStatus { get; set; } = OperationApprovalStatus.NotRequired;
    public ErpIntegrationStatus ErpIntegrationStatus { get; set; } = ErpIntegrationStatus.Pending;
    public DateTimeOffset? PlannedWarehouseOutboundAtUtc { get; set; }
    public DateTimeOffset? ShippedAtUtc { get; set; }
    public string? ExternalReferenceNo { get; set; }
    public string? WaybillNo { get; set; }
    public bool IsEDispatch { get; set; }
    public string? CarrierCode { get; set; }
    public string? CarrierName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? DriverName { get; set; }
    public string? SealNo { get; set; }
    public string? TrackingNo { get; set; }
    public string? ProjectCode { get; set; }
    public string? CostCenterCode { get; set; }
    public string? MovementTypeCode { get; set; }
    public string? ExitLocationCode { get; set; }
    public byte Priority { get; set; } = 3;
    public string? Description { get; set; }

    // Policy snapshot
    public bool RequireApproval { get; set; }
    public bool RequireAssignee { get; set; } = true;
    public bool AllowPartialPicking { get; set; } = true;
    public bool AllowPartialWarehouseOutbound { get; set; } = true;
    public bool RequireSourceLocation { get; set; } = true;
    public bool RequireWarehouseOutboundInformation { get; set; }
    public bool RequireLoadingConfirmation { get; set; } = true;
    public bool AutoReleaseTaskBased { get; set; }
    public bool AutoPostErpAfterApproval { get; set; }
    public decimal MinimumFulfillmentPercent { get; set; } = 100;
    public decimal OverPickTolerancePercent { get; set; }
    public WarehouseOutboundReservationPolicy ReservationPolicy { get; set; } = WarehouseOutboundReservationPolicy.OnRelease;
    public WarehouseOutboundPackingPolicy PackingPolicy { get; set; } = WarehouseOutboundPackingPolicy.Optional;
    public WarehouseOutboundShortagePolicy ShortagePolicy { get; set; } = WarehouseOutboundShortagePolicy.AllowPartial;
    public WarehouseOutboundOverPickPolicy OverPickPolicy { get; set; } = WarehouseOutboundOverPickPolicy.Block;
    public byte[] RowVersion { get; set; } = [];
    public ICollection<WarehouseOutboundSourceDocument> SourceDocuments { get; set; } = [];
    public ICollection<WarehouseOutboundLine> Lines { get; set; } = [];
    public ICollection<WarehouseOutboundTask> Tasks { get; set; } = [];
    public ICollection<WarehouseOutboundStatusHistory> StatusHistory { get; set; } = [];
}

public sealed class WarehouseOutboundSourceDocument : BaseEntity
{
    public long WarehouseOutboundHeaderId { get; set; }
    public WarehouseOutboundHeader Header { get; set; } = null!;
    public string SourceDocumentType { get; set; } = "SalesOrder";
    public string ExternalDocumentNo { get; set; } = string.Empty;
    public string? ExternalDocumentId { get; set; }
    public DateOnly? ExternalDocumentDate { get; set; }
    public string? ExternalStatus { get; set; }
    public ICollection<WarehouseOutboundLineSource> LineSources { get; set; } = [];
}

public sealed class WarehouseOutboundLine : BaseEntity
{
    public long WarehouseOutboundHeaderId { get; set; }
    public WarehouseOutboundHeader Header { get; set; } = null!;
    public int LineNo { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal PackedQuantity { get; set; }
    public decimal LoadedQuantity { get; set; }
    public decimal ShippedQuantity { get; set; }
    public decimal ShortClosedQuantity { get; set; }
    public StockTrackingType TrackingType { get; set; }
    public bool RequireHandlingUnit { get; set; }
    public long? DefaultSourceLocationId { get; set; }
    public WarehouseOutboundLineStatus Status { get; set; } = WarehouseOutboundLineStatus.Open;
    public string? Description { get; set; }
    public string? ProjectCode { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<WarehouseOutboundLineSource> Sources { get; set; } = [];
    public ICollection<WarehouseOutboundTracking> Trackings { get; set; } = [];
    public ICollection<WarehouseOutboundTaskLine> TaskLines { get; set; } = [];
}

public sealed class WarehouseOutboundLineSource : BaseEntity
{
    public long WarehouseOutboundLineId { get; set; }
    public WarehouseOutboundLine Line { get; set; } = null!;
    public long WarehouseOutboundSourceDocumentId { get; set; }
    public WarehouseOutboundSourceDocument SourceDocument { get; set; } = null!;
    public string ExternalLineId { get; set; } = string.Empty;
    public int? ExternalLineNo { get; set; }
    public string ExternalStockCode { get; set; } = string.Empty;
    public string? ExternalYapCode { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal PreviouslyShippedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
}

public sealed class WarehouseOutboundTracking : BaseEntity
{
    public long WarehouseOutboundLineId { get; set; }
    public WarehouseOutboundLine Line { get; set; } = null!;
    public string? HandlingUnitNo { get; set; }
    public string? ContainerNo { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal PackedQuantity { get; set; }
    public decimal LoadedQuantity { get; set; }
    public decimal ShippedQuantity { get; set; }
    public long? SourceLocationId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class WarehouseOutboundTask : BaseEntity
{
    public long WarehouseOutboundHeaderId { get; set; }
    public WarehouseOutboundHeader Header { get; set; } = null!;
    public string TaskNo { get; set; } = string.Empty;
    public WarehouseOutboundTaskType TaskType { get; set; }
    public long WarehouseId { get; set; }
    public WarehouseOutboundTaskStatus Status { get; set; }
    public byte Priority { get; set; }
    public DateTimeOffset? PlannedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<WarehouseOutboundTaskLine> Lines { get; set; } = [];
    public ICollection<WarehouseOutboundTaskAssignment> Assignments { get; set; } = [];
}

public sealed class WarehouseOutboundTaskLine : BaseEntity
{
    public long WarehouseOutboundTaskId { get; set; }
    public WarehouseOutboundTask Task { get; set; } = null!;
    public long WarehouseOutboundLineId { get; set; }
    public WarehouseOutboundLine Line { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal ProcessedQuantity { get; set; }
    public long? SourceLocationId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class WarehouseOutboundTaskAssignment : BaseEntity
{
    public long WarehouseOutboundTaskId { get; set; }
    public WarehouseOutboundTask Task { get; set; } = null!;
    public long UserId { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; }
    public long AssignedBy { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
}

public sealed class WarehouseOutboundStatusHistory : BaseEntity
{
    public long WarehouseOutboundHeaderId { get; set; }
    public WarehouseOutboundHeader Header { get; set; } = null!;
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public long ChangedBy { get; set; }
    public Guid CorrelationId { get; set; }
}

public sealed class WarehouseOutboundPolicy : BaseEntity
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
    public bool AllowPartialPicking { get; set; } = true;
    public bool AllowPartialWarehouseOutbound { get; set; } = true;
    public bool RequireSourceLocation { get; set; } = true;
    public bool RequireWarehouseOutboundInformation { get; set; }
    public bool RequireLoadingConfirmation { get; set; } = true;
    public bool AutoPostErpAfterApproval { get; set; }
    public decimal MinimumFulfillmentPercent { get; set; } = 100;
    public decimal OverPickTolerancePercent { get; set; }
    public WarehouseOutboundReservationPolicy ReservationPolicy { get; set; } = WarehouseOutboundReservationPolicy.OnRelease;
    public WarehouseOutboundPackingPolicy PackingPolicy { get; set; } = WarehouseOutboundPackingPolicy.Optional;
    public WarehouseOutboundShortagePolicy ShortagePolicy { get; set; } = WarehouseOutboundShortagePolicy.AllowPartial;
    public WarehouseOutboundOverPickPolicy OverPickPolicy { get; set; } = WarehouseOutboundOverPickPolicy.Block;
    public byte[] RowVersion { get; set; } = [];
}
