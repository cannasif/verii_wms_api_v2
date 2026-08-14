using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Quality.Domain;

public enum QualityInspectionMode { NoCheck = 1, QuickCheck = 2, InspectionRequired = 3 }
public enum QualityFailAction { Quarantine = 1, Reject = 2, ReturnToSupplier = 3, ManagerApproval = 4 }
public enum QualitySamplingMode { All = 1, Percentage = 2, FixedQuantity = 3, EveryNthHandlingUnit = 4 }
public enum QualityInspectionStatus { Pending = 1, InProgress = 2, PartiallyDecided = 3, Passed = 4, Failed = 5, Quarantined = 6, Released = 7, Cancelled = 8 }
public enum QualityDecision { Pending = 1, Accepted = 2, Rejected = 3, Quarantined = 4, Returned = 5, Hold = 6 }

/// <summary>
/// Branch-scoped, reusable reason catalogue used while applying a quality decision.
/// The selected code is also copied to the decision evidence as a snapshot so historical
/// records remain readable when a definition is renamed or deactivated later.
/// </summary>
public sealed class QualityDecisionCode : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public QualityDecision? ApplicableDecision { get; set; }
    public bool RequiresNote { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}
public enum QualityInspectionWorkStopReason
{
    Break = 1,
    MaterialWait = 2,
    EquipmentIssue = 3,
    DocumentationWait = 4,
    SupervisorWait = 5,
    ShiftEnd = 6,
    Handover = 7,
    Other = 8,
    DecisionApplied = 9,
    InspectionCancelled = 10
}
public enum QualityInspectionWorkState { NotStarted = 1, Running = 2, Paused = 3, Completed = 4 }

public sealed class QualityParameter : BaseEntity
{
    public string ParameterKey { get; set; } = "DEFAULT";
    public bool AutoCreateInspectionOnReceipt { get; set; } = true;
    public QualityInspectionMode DefaultInspectionMode { get; set; } = QualityInspectionMode.NoCheck;
    public QualityFailAction DefaultFailAction { get; set; } = QualityFailAction.Quarantine;
    public bool HoldInventoryUntilDecision { get; set; } = true;
    public bool BlockPutawayUntilDecision { get; set; } = true;
    public bool BlockErpPostingUntilDecision { get; set; } = true;
    public bool RequireManagerApprovalForRelease { get; set; }
    public bool AllowPartialDecision { get; set; } = true;
    public bool AllowDirectReceiptWhenNoRule { get; set; } = true;
    public bool BlockReceiptWhenLotMissing { get; set; }
    public bool BlockReceiptWhenSerialMissing { get; set; }
    public bool BlockReceiptWhenExpiryMissing { get; set; }
    public long? DefaultQualityLocationId { get; set; }
    public long? DefaultAcceptedLocationId { get; set; }
    public long? DefaultQuarantineLocationId { get; set; }
    public long? DefaultRejectLocationId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<QualityQuarantineDestination> QuarantineDestinations { get; set; } = [];
    public ICollection<QualityWarehouseRoute> WarehouseRoutes { get; set; } = [];
}

public sealed class QualityQuarantineDestination : BaseEntity
{
    public long QualityParameterId { get; set; }
    public QualityParameter QualityParameter { get; set; } = null!;
    public long LocationId { get; set; }
    public int Priority { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Source warehouse specific quality movement defaults. Nullable target fields inherit the
/// corresponding branch-level value from <see cref="QualityParameter"/>.
/// </summary>
public sealed class QualityWarehouseRoute : BaseEntity
{
    public long QualityParameterId { get; set; }
    public QualityParameter QualityParameter { get; set; } = null!;
    public long SourceWarehouseId { get; set; }
    public long? QualityLocationId { get; set; }
    public long? AcceptedLocationId { get; set; }
    public long? QuarantineLocationId { get; set; }
    public long? RejectLocationId { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class QualityRule : BaseEntity
{
    public string ScopeType { get; set; } = QualityRuleScopeTypes.Stock;
    public long? StockId { get; set; }
    public string? StockGroupCode { get; set; }
    public QualityInspectionMode InspectionMode { get; set; } = QualityInspectionMode.InspectionRequired;
    public QualitySamplingMode SamplingMode { get; set; } = QualitySamplingMode.All;
    public decimal SamplingValue { get; set; } = 100m;
    public QualityFailAction FailAction { get; set; } = QualityFailAction.Quarantine;
    public bool AutoQuarantine { get; set; } = true;
    public bool RequireLot { get; set; }
    public bool RequireSerial { get; set; }
    public bool RequireExpiryDate { get; set; }
    public int? MinimumRemainingShelfLifeDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class QualityRuleScopeTypes
{
    public const string Stock = "Stock";
    public const string StockGroup = "StockGroup";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Stock, StockGroup };
}

public sealed class QualityInspection : BaseEntity
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public string InspectionNo { get; set; } = string.Empty;
    public string SourceDocumentType { get; set; } = string.Empty;
    public long SourceDocumentId { get; set; }
    public string SourceDocumentNo { get; set; } = string.Empty;
    public long WarehouseId { get; set; }
    public long? SupplierId { get; set; }
    public bool IsPriority { get; set; }
    public QualityInspectionStatus Status { get; set; } = QualityInspectionStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QueuedAtUtc { get; set; }
    public long? QueuedBy { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public long? InspectorUserId { get; set; }
    public string? Note { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<QualityInspectionLine> Lines { get; set; } = [];
    public ICollection<QualityInspectionDisposition> Dispositions { get; set; } = [];
    public ICollection<QualityInspectionControl> Controls { get; set; } = [];
    public ICollection<QualityInspectionWorkSession> WorkSessions { get; set; } = [];
}

/// <summary>
/// One uninterrupted GKK work interval. Closed sessions are operational evidence and are never
/// reused; another operator continues the same inspection by opening a new session.
/// </summary>
public sealed class QualityInspectionWorkSession : BaseEntity
{
    public long QualityInspectionId { get; set; }
    public QualityInspection QualityInspection { get; set; } = null!;
    public int SequenceNo { get; set; }
    public long WorkerUserId { get; set; }
    public string WorkerNameSnapshot { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public long DurationSeconds { get; set; }
    public QualityInspectionWorkStopReason? StopReason { get; set; }
    public string? StopNote { get; set; }
    public Guid StartIdempotencyKey { get; set; }
    public Guid? EndIdempotencyKey { get; set; }
    public long? EndedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class QualityInspectionLine : BaseEntity
{
    public long QualityInspectionId { get; set; }
    public QualityInspection Inspection { get; set; } = null!;
    public long? GoodsReceiptLineId { get; set; }
    public long? WarehouseInboundLineId { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal SampleQuantity { get; set; }
    /// <summary>
    /// Cumulative quantity physically checked by GKK operators. This is deliberately kept
    /// separate from accepted/rejected quantities: a 100-unit lot can be accepted after a
    /// 10-unit sample has actually been inspected.
    /// </summary>
    public decimal InspectedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal QuarantineQuantity { get; set; }
    public long? QuarantineLocationId { get; set; }
    public QualityDecision Decision { get; set; } = QualityDecision.Pending;
    public long? DecisionCodeId { get; set; }
    public QualityDecisionCode? DecisionCode { get; set; }
    public string? ReasonCode { get; set; }
    public string? ReasonNote { get; set; }
    public long? DecisionBy { get; set; }
    public DateTimeOffset? DecisionAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<QualityInspectionDisposition> Dispositions { get; set; } = [];
    public ICollection<QualityInspectionControl> Controls { get; set; } = [];
}

/// <summary>
/// Immutable evidence of how much material was physically inspected for one GKK decision.
/// The line keeps the cumulative projection; this table preserves each decision-time fact.
/// </summary>
public sealed class QualityInspectionControl : BaseEntity
{
    public long QualityInspectionId { get; set; }
    public QualityInspection QualityInspection { get; set; } = null!;
    public long QualityInspectionLineId { get; set; }
    public QualityInspectionLine QualityInspectionLine { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public decimal LotQuantitySnapshot { get; set; }
    public decimal RequiredQuantitySnapshot { get; set; }
    public decimal InspectedQuantity { get; set; }
    public string OutcomeSummary { get; set; } = string.Empty;
    public string? Note { get; set; }
    public long InspectedBy { get; set; }
    public DateTimeOffset InspectedAtUtc { get; set; }
}

/// <summary>
/// Immutable execution evidence for each partial quality decision. A single inspection line can be
/// distributed to multiple decisions and warehouse/location targets in the same request.
/// </summary>
public sealed class QualityInspectionDisposition : BaseEntity
{
    public long QualityInspectionId { get; set; }
    public QualityInspection QualityInspection { get; set; } = null!;
    public long QualityInspectionLineId { get; set; }
    public QualityInspectionLine QualityInspectionLine { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public int SequenceNo { get; set; }
    public QualityDecision Decision { get; set; }
    public decimal Quantity { get; set; }
    public long SourceWarehouseId { get; set; }
    public long SourceLocationId { get; set; }
    public long TargetWarehouseId { get; set; }
    public long TargetLocationId { get; set; }
    public string SourceWarehouseCodeSnapshot { get; set; } = string.Empty;
    public string SourceLocationCodeSnapshot { get; set; } = string.Empty;
    public string TargetWarehouseCodeSnapshot { get; set; } = string.Empty;
    public string TargetLocationCodeSnapshot { get; set; } = string.Empty;
    public string SourceStockStatus { get; set; } = string.Empty;
    public string TargetStockStatus { get; set; } = string.Empty;
    public long? StockMovementOperationId { get; set; }
    public long? WarehouseTransferId { get; set; }
    public long? DecisionCodeId { get; set; }
    public QualityDecisionCode? DecisionCode { get; set; }
    public string? ReasonCode { get; set; }
    public string? ReasonNote { get; set; }
    public long DecisionBy { get; set; }
    public DateTimeOffset DecisionAtUtc { get; set; }
}
