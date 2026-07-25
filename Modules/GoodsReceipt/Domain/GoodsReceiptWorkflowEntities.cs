using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Domain;

public enum GoodsReceiptTaskType
{
    Receive = 1,
    QualityMove = 2,
    QualityCheck = 3,
    Putaway = 4,
    ReturnToSupplier = 5
}

public enum GoodsReceiptTaskStatus
{
    Draft = 1,
    Released = 2,
    Assigned = 3,
    InProgress = 4,
    Paused = 5,
    PartiallyCompleted = 6,
    Completed = 7,
    Cancelled = 8
}

public enum GoodsReceiptAssignmentRole
{
    Owner = 1,
    Worker = 2,
    Supervisor = 3,
    Observer = 4
}

public enum GoodsReceiptAssignmentStatus
{
    Assigned = 1,
    Accepted = 2,
    InProgress = 3,
    Completed = 4,
    Unassigned = 5,
    Rejected = 6
}

public enum GoodsReceiptLabelBatchStatus
{
    Draft = 1,
    Generated = 2,
    PartiallyPrinted = 3,
    Printed = 4,
    PartiallyConsumed = 5,
    Consumed = 6,
    Cancelled = 7
}

public enum GoodsReceiptLabelStatus
{
    Generated = 1,
    Printed = 2,
    Assigned = 3,
    Consumed = 4,
    Void = 5
}

public sealed class GoodsReceiptTask : BaseEntity
{
    public long GrHeaderId { get; set; }
    public GoodsReceiptHeader Header { get; set; } = null!;
    public string TaskNo { get; set; } = string.Empty;
    public GoodsReceiptTaskType TaskType { get; set; } = GoodsReceiptTaskType.Receive;
    public GoodsReceiptTaskStatus Status { get; set; } = GoodsReceiptTaskStatus.Draft;
    public byte Priority { get; set; } = 3;
    public long WarehouseId { get; set; }
    public string? ZoneCode { get; set; }
    public DateTimeOffset? PlannedStartAtUtc { get; set; }
    public DateTimeOffset? DueAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public long? ReleasedBy { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GoodsReceiptTaskLine> Lines { get; set; } = [];
    public ICollection<GoodsReceiptTaskAssignment> Assignments { get; set; } = [];
}

public sealed class GoodsReceiptTaskLine : BaseEntity
{
    public long GrTaskId { get; set; }
    public GoodsReceiptTask Task { get; set; } = null!;
    public long GrLineId { get; set; }
    public GoodsReceiptLine Line { get; set; } = null!;
    public int SequenceNo { get; set; }
    public long? FromLocationId { get; set; }
    public long? ToLocationId { get; set; }
    public long? HandlingUnitId { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ProcessedQuantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public GoodsReceiptTaskStatus Status { get; set; } = GoodsReceiptTaskStatus.Draft;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GoodsReceiptTaskLineTracking> Trackings { get; set; } = [];
}

/// <summary>
/// Emir aşamasında planlanan lot/seri dağılımıdır. Fiziksel kabul kanıtı değildir;
/// gerçekleşen kayıt GoodsReceiptExecutionLine üzerinde immutable olarak oluşur.
/// </summary>
public sealed class GoodsReceiptTaskLineTracking : BaseEntity
{
    public long GrTaskLineId { get; set; }
    public GoodsReceiptTaskLine TaskLine { get; set; } = null!;
    public int SequenceNo { get; set; }
    public long StockId { get; set; }
    public decimal PlannedQuantity { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public long TargetWarehouseId { get; set; }
    public long ToLocationId { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GoodsReceiptTaskAssignment : BaseEntity
{
    public long GrTaskId { get; set; }
    public GoodsReceiptTask Task { get; set; } = null!;
    public long UserId { get; set; }
    public GoodsReceiptAssignmentRole AssignmentRole { get; set; } = GoodsReceiptAssignmentRole.Worker;
    public GoodsReceiptAssignmentStatus Status { get; set; } = GoodsReceiptAssignmentStatus.Assigned;
    public DateTimeOffset AssignedAtUtc { get; set; }
    public long? AssignedBy { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? UnassignedAtUtc { get; set; }
    public string? UnassignedReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GoodsReceiptLabelBatch : BaseEntity
{
    public long GrHeaderId { get; set; }
    public GoodsReceiptHeader Header { get; set; } = null!;
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public string BatchNo { get; set; } = string.Empty;
    public GoodsReceiptLabelBatchStatus Status { get; set; } = GoodsReceiptLabelBatchStatus.Draft;
    public int TotalLabelCount { get; set; }
    public int PrintedLabelCount { get; set; }
    public int ConsumedLabelCount { get; set; }
    public int VoidLabelCount { get; set; }
    public DateTimeOffset? LastPrintedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GoodsReceiptLabel> Labels { get; set; } = [];
}

public sealed class GoodsReceiptLabel : BaseEntity
{
    public long BatchId { get; set; }
    public GoodsReceiptLabelBatch Batch { get; set; } = null!;
    public long GrHeaderId { get; set; }
    public long? GrLineId { get; set; }
    public long? GrTaskLineId { get; set; }
    public long? StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public decimal LabelQuantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public string BarcodeValue { get; set; } = string.Empty;
    public GoodsReceiptLabelStatus Status { get; set; } = GoodsReceiptLabelStatus.Generated;
    public int PrintCount { get; set; }
    public DateTimeOffset? LastPrintedAtUtc { get; set; }
    public DateTimeOffset? AssignedAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public string? VoidReason { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
