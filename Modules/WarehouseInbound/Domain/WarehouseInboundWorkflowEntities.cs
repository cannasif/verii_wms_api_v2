using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Domain;

public enum WarehouseInboundTaskType
{
    Receive = 1,
    QualityMove = 2,
    QualityCheck = 3,
    Putaway = 4,
    ReturnToSupplier = 5
}

public enum WarehouseInboundTaskStatus
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

public enum WarehouseInboundAssignmentRole
{
    Owner = 1,
    Worker = 2,
    Supervisor = 3,
    Observer = 4
}

public enum WarehouseInboundAssignmentStatus
{
    Assigned = 1,
    Accepted = 2,
    InProgress = 3,
    Completed = 4,
    Unassigned = 5,
    Rejected = 6
}

public enum WarehouseInboundLabelBatchStatus
{
    Draft = 1,
    Generated = 2,
    PartiallyPrinted = 3,
    Printed = 4,
    PartiallyConsumed = 5,
    Consumed = 6,
    Cancelled = 7
}

public enum WarehouseInboundLabelStatus
{
    Generated = 1,
    Printed = 2,
    Assigned = 3,
    Consumed = 4,
    Void = 5
}

public sealed class WarehouseInboundTask : BaseEntity
{
    public long GrHeaderId { get; set; }
    public WarehouseInboundHeader Header { get; set; } = null!;
    public string TaskNo { get; set; } = string.Empty;
    public WarehouseInboundTaskType TaskType { get; set; } = WarehouseInboundTaskType.Receive;
    public WarehouseInboundTaskStatus Status { get; set; } = WarehouseInboundTaskStatus.Draft;
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
    public ICollection<WarehouseInboundTaskLine> Lines { get; set; } = [];
    public ICollection<WarehouseInboundTaskAssignment> Assignments { get; set; } = [];
}

public sealed class WarehouseInboundTaskLine : BaseEntity
{
    public long GrTaskId { get; set; }
    public WarehouseInboundTask Task { get; set; } = null!;
    public long GrLineId { get; set; }
    public WarehouseInboundLine Line { get; set; } = null!;
    public int SequenceNo { get; set; }
    public long? FromLocationId { get; set; }
    public long? ToLocationId { get; set; }
    public long? HandlingUnitId { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ProcessedQuantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public WarehouseInboundTaskStatus Status { get; set; } = WarehouseInboundTaskStatus.Draft;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<WarehouseInboundTaskLineTracking> Trackings { get; set; } = [];
}

/// <summary>
/// Emir aşamasında planlanan lot/seri dağılımıdır. Fiziksel kabul kanıtı değildir;
/// gerçekleşen kayıt WarehouseInboundExecutionLine üzerinde immutable olarak oluşur.
/// </summary>
public sealed class WarehouseInboundTaskLineTracking : BaseEntity
{
    public long GrTaskLineId { get; set; }
    public WarehouseInboundTaskLine TaskLine { get; set; } = null!;
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

public sealed class WarehouseInboundTaskAssignment : BaseEntity
{
    public long GrTaskId { get; set; }
    public WarehouseInboundTask Task { get; set; } = null!;
    public long UserId { get; set; }
    public WarehouseInboundAssignmentRole AssignmentRole { get; set; } = WarehouseInboundAssignmentRole.Worker;
    public WarehouseInboundAssignmentStatus Status { get; set; } = WarehouseInboundAssignmentStatus.Assigned;
    public DateTimeOffset AssignedAtUtc { get; set; }
    public long? AssignedBy { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? UnassignedAtUtc { get; set; }
    public string? UnassignedReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class WarehouseInboundLabelBatch : BaseEntity
{
    public long GrHeaderId { get; set; }
    public WarehouseInboundHeader Header { get; set; } = null!;
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public string BatchNo { get; set; } = string.Empty;
    public WarehouseInboundLabelBatchStatus Status { get; set; } = WarehouseInboundLabelBatchStatus.Draft;
    public int TotalLabelCount { get; set; }
    public int PrintedLabelCount { get; set; }
    public int ConsumedLabelCount { get; set; }
    public int VoidLabelCount { get; set; }
    public DateTimeOffset? LastPrintedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<WarehouseInboundLabel> Labels { get; set; } = [];
}

public sealed class WarehouseInboundLabel : BaseEntity
{
    public long BatchId { get; set; }
    public WarehouseInboundLabelBatch Batch { get; set; } = null!;
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
    public WarehouseInboundLabelStatus Status { get; set; } = WarehouseInboundLabelStatus.Generated;
    public int PrintCount { get; set; }
    public DateTimeOffset? LastPrintedAtUtc { get; set; }
    public DateTimeOffset? AssignedAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public string? VoidReason { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
