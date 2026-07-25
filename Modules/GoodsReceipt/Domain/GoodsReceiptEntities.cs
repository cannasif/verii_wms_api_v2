using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Domain;

public enum GoodsReceiptType
{
    PurchaseOrder = 1,
    Direct = 2,
    TransferIn = 3,
    CustomerReturn = 4,
    ProductionReturn = 5,
    SteelPlate = 6
}

public enum GoodsReceiptSourceDocumentType
{
    PurchaseOrder = 1,
    AdvanceShippingNotice = 2,
    TransferOrder = 3,
    CustomerReturnOrder = 4,
    ProductionOrder = 5,
    SupplierWaybill = 6,
    ElectronicWaybill = 7
}

public enum GoodsReceiptLineStatus
{
    Open = 1,
    PartiallyReceived = 2,
    Received = 3,
    ShortClosed = 4,
    Cancelled = 5
}

public enum GoodsReceiptInitiationMode
{
    OrderBasedTask = 1,
    UnplannedTask = 2,
    DirectReceipt = 3
}

/// <summary>
/// Raporlama ve süreç yönlendirmesi için mal kabulün sipariş/emir eksenindeki açık sınıflandırmasıdır.
/// ReceiptType iş kaynağını, InitiationMode teknik başlangıç biçimini; ProcessType ise iş senaryosunu ifade eder.
/// </summary>
public enum GoodsReceiptProcessType
{
    OrderBasedTask = 1,
    OrderlessTask = 2,
    OrderBasedDirectReceipt = 3,
    OrderlessDirectReceipt = 4
}

public enum GoodsReceiptLabelStrategy
{
    None = 1,
    PreGenerate = 2,
    SupplierLabel = 3,
    GenerateOnReceipt = 4
}

public enum GoodsReceiptStatusArea
{
    Operation = 1,
    Approval = 2,
    Quality = 3,
    Putaway = 4,
    ErpIntegration = 5
}

public sealed class GoodsReceiptHeader : BaseEntity, IWarehouseOperationHeader
{
    public long DocumentSeriesId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public GoodsReceiptType ReceiptType { get; set; } = GoodsReceiptType.PurchaseOrder;
    public GoodsReceiptInitiationMode InitiationMode { get; set; } = GoodsReceiptInitiationMode.OrderBasedTask;
    public GoodsReceiptProcessType ProcessType { get; set; } = GoodsReceiptProcessType.OrderBasedTask;
    public GoodsReceiptLabelStrategy LabelStrategy { get; set; } = GoodsReceiptLabelStrategy.None;
    public WarehouseOperationSourceSystem SourceSystem { get; set; } = WarehouseOperationSourceSystem.Manual;
    public string? ExternalReferenceNo { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public long? SupplierId { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public string? SupplierTaxNoSnapshot { get; set; }

    public long TargetWarehouseId { get; set; }
    public long ReceivingLocationId { get; set; }
    public string? DefaultPutawayZoneCode { get; set; }
    public long? QualityLocationId { get; set; }
    public long? QuarantineLocationId { get; set; }

    public WarehouseOperationStatus Status { get; set; } = WarehouseOperationStatus.Draft;
    public OperationApprovalStatus ApprovalStatus { get; set; } = OperationApprovalStatus.NotRequired;
    public OperationQualityStatus QualityStatus { get; set; } = OperationQualityStatus.NotRequired;
    public OperationPutawayStatus PutawayStatus { get; set; } = OperationPutawayStatus.Pending;
    public ErpIntegrationStatus ErpIntegrationStatus { get; set; } = ErpIntegrationStatus.Pending;

    public DateTimeOffset? PlannedArrivalAtUtc { get; set; }
    public DateTimeOffset? ActualArrivalAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public long? ReleasedBy { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public long? StartedBy { get; set; }
    public DateTimeOffset? ReceivedAtUtc { get; set; }
    public long? ReceivedBy { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public long? CompletedBy { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public long? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }

    public string? WaybillNo { get; set; }
    public DateOnly? WaybillDate { get; set; }
    public string? ElectronicWaybillNo { get; set; }
    public string? ShipmentReferenceNo { get; set; }
    public string? CarrierCode { get; set; }
    public string? CarrierName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? DriverName { get; set; }
    public string? SealNo { get; set; }

    public bool AllowOverReceipt { get; set; }
    public decimal OverReceiptTolerancePercent { get; set; }
    public bool AllowUnderReceipt { get; set; } = true;
    public bool RequireShortCloseApproval { get; set; }
    public bool RequireQualityControl { get; set; }
    public bool RequirePutaway { get; set; } = true;
    public bool RequireHandlingUnit { get; set; }
    public OverReceiptPolicy OverReceiptPolicy { get; set; } = OverReceiptPolicy.NotAllowed;
    public bool RequireReceiptApproval { get; set; }
    public bool RequireQualityApproval { get; set; }
    public bool RequireErpApproval { get; set; }
    public bool HoldInventoryUntilQualityDecision { get; set; }
    public bool BlockPutawayUntilQualityDecision { get; set; }
    public InventoryAvailabilityPolicy InventoryAvailabilityPolicy { get; set; } = InventoryAvailabilityPolicy.AfterQualityApproval;
    public GoodsReceiptErpPostingPolicy ErpPostingPolicy { get; set; } = GoodsReceiptErpPostingPolicy.AfterAllApprovals;
    public byte Priority { get; set; } = 3;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ICollection<GoodsReceiptSourceDocument> SourceDocuments { get; set; } = [];
    public ICollection<GoodsReceiptLine> Lines { get; set; } = [];
    public ICollection<GoodsReceiptStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<GoodsReceiptTask> Tasks { get; set; } = [];
    public ICollection<GoodsReceiptLabelBatch> LabelBatches { get; set; } = [];
}

public sealed class GoodsReceiptSourceDocument : BaseEntity
{
    public long GrHeaderId { get; set; }
    public GoodsReceiptHeader Header { get; set; } = null!;
    public GoodsReceiptSourceDocumentType SourceDocumentType { get; set; }
    public WarehouseOperationSourceSystem SourceSystem { get; set; } = WarehouseOperationSourceSystem.Netsis;
    public string? ExternalDocumentId { get; set; }
    public string ExternalDocumentNo { get; set; } = string.Empty;
    public DateOnly? ExternalDocumentDate { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTimeOffset? LastSynchronizedAtUtc { get; set; }
    public string? ExternalVersion { get; set; }
    public string? ExternalStatus { get; set; }
    public ICollection<GoodsReceiptLineSource> LineSources { get; set; } = [];
}

public sealed class GoodsReceiptLine : BaseEntity, IWarehouseOperationLine
{
    public long GrHeaderId { get; set; }
    public GoodsReceiptHeader Header { get; set; } = null!;
    public int LineNo { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string BaseUnitCode { get; set; } = string.Empty;
    public decimal UnitConversionFactor { get; set; } = 1m;

    public decimal ExpectedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal QuarantineQuantity { get; set; }
    public decimal PutawayQuantity { get; set; }
    public decimal ShortClosedQuantity { get; set; }
    decimal IWarehouseOperationLine.ProcessedQuantity => ReceivedQuantity;

    public StockTrackingType TrackingType { get; set; } = StockTrackingType.None;
    public bool RequireLot { get; set; }
    public bool RequireSerial { get; set; }
    public bool RequireManufacturingDate { get; set; }
    public bool RequireExpirationDate { get; set; }
    public int? MinimumShelfLifeDays { get; set; }
    public bool RequireQualityControl { get; set; }
    public bool RequireHandlingUnit { get; set; }

    /// <summary>Header deposu varsayılandır; gerçek operasyon hedefi satırda saklanır.</summary>
    public long TargetWarehouseId { get; set; }

    public GoodsReceiptLineStatus Status { get; set; } = GoodsReceiptLineStatus.Open;
    public bool AllowOverReceipt { get; set; }
    public decimal OverReceiptTolerancePercent { get; set; }
    public bool AllowUnderReceipt { get; set; } = true;
    public long? DefaultReceivingLocationId { get; set; }
    public long? DefaultPutawayLocationId { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GoodsReceiptLineSource> Sources { get; set; } = [];
}

public sealed class GoodsReceiptLineSource : BaseEntity
{
    public long GrLineId { get; set; }
    public GoodsReceiptLine Line { get; set; } = null!;
    public long GrSourceDocumentId { get; set; }
    public GoodsReceiptSourceDocument SourceDocument { get; set; } = null!;
    public string ExternalLineId { get; set; } = string.Empty;
    public int? ExternalLineNo { get; set; }
    public string ExternalStockCode { get; set; } = string.Empty;
    public string? ExternalYapCode { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal PreviouslyReceivedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string? ExternalStatus { get; set; }
}

public sealed class GoodsReceiptStatusHistory : BaseEntity
{
    public long GrHeaderId { get; set; }
    public GoodsReceiptHeader Header { get; set; } = null!;
    public GoodsReceiptStatusArea StatusArea { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; set; }
    public long? ChangedBy { get; set; }
    public string? ReasonCode { get; set; }
    public string? Description { get; set; }
    public Guid CorrelationId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
}
