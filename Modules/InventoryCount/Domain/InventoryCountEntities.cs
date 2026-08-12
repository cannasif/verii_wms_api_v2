using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.InventoryCount.Domain;

public enum InventoryCountType
{
    FullPhysical = 1,
    Cycle = 2,
    Spot = 3,
    ZeroCheck = 4,
    Partial = 5
}

public enum InventoryCountMode
{
    Blind = 1,
    Open = 2,
    DoubleBlind = 3
}

public enum InventoryCountMovementPolicy
{
    Snapshot = 1,
    SnapshotWithMovementReconciliation = 2,
    LocationFreeze = 3
}

public enum InventoryCountStatus
{
    Draft = 1,
    Planned = 2,
    Released = 3,
    InProgress = 4,
    AwaitingReview = 5,
    RecountRequired = 6,
    AwaitingApproval = 7,
    Posting = 8,
    Completed = 9,
    Cancelled = 10
}

public enum InventoryCountTaskStatus
{
    Ready = 1,
    Assigned = 2,
    InProgress = 3,
    AwaitingReview = 4,
    RecountRequired = 5,
    Completed = 6,
    Cancelled = 7
}

public enum InventoryCountLineStatus
{
    Pending = 1,
    Counting = 2,
    Matched = 3,
    Variance = 4,
    RecountRequired = 5,
    Approved = 6,
    Posted = 7,
    Cancelled = 8
}

public enum InventoryCountEntryType
{
    Quantity = 1,
    Barcode = 2,
    ZeroConfirmation = 3,
    UnexpectedStock = 4
}

public enum InventoryCountReviewAction
{
    Approve = 1,
    RejectAndRecount = 2,
    RejectAndCancel = 3,
    OverrideTolerance = 4
}

public sealed class InventoryCountHeader : BaseEntity
{
    public Guid CountCode { get; set; } = Guid.NewGuid();
    public long? DocumentSeriesId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public InventoryCountType CountType { get; set; } = InventoryCountType.Cycle;
    public InventoryCountMode CountMode { get; set; } = InventoryCountMode.Blind;
    public InventoryCountMovementPolicy MovementPolicy { get; set; } = InventoryCountMovementPolicy.SnapshotWithMovementReconciliation;
    public InventoryCountStatus Status { get; set; } = InventoryCountStatus.Draft;
    public long WarehouseId { get; set; }
    public int Priority { get; set; } = 3;
    public DateTime? PlannedStartUtc { get; set; }
    public DateTime? PlannedEndUtc { get; set; }
    public decimal QuantityTolerance { get; set; }
    public decimal PercentageTolerance { get; set; }
    public int MaxCountAttempts { get; set; } = 2;
    public bool RequireIndependentRecount { get; set; } = true;
    public bool AllowUnexpectedStock { get; set; } = true;
    public bool AutoApproveWithinTolerance { get; set; } = true;
    public bool IncludeEmptyLocations { get; set; }
    public string? Description { get; set; }
    public long? SnapshotMovementEntryId { get; set; }
    public string? ReleaseIdempotencyKey { get; set; }
    public DateTime? SnapshotAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public long? ReleasedByUserId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? CompletedByUserId { get; set; }
    public int TaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
    public int LineCount { get; set; }
    public int CountedLineCount { get; set; }
    public int VarianceLineCount { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<InventoryCountScope> Scopes { get; set; } = [];
    public ICollection<InventoryCountTask> Tasks { get; set; } = [];
    public ICollection<InventoryCountLine> Lines { get; set; } = [];
}

public sealed class InventoryCountScope : BaseEntity
{
    public long HeaderId { get; set; }
    public InventoryCountHeader Header { get; set; } = null!;
    public int SequenceNo { get; set; }
    public long? LocationId { get; set; }
    public long? StockId { get; set; }
    public long? YapCodeId { get; set; }
    public string? StockGroupCode { get; set; }
    public bool IncludeDescendantLocations { get; set; } = true;
    public bool IncludeEmptyLocations { get; set; }
}

public sealed class InventoryCountTask : BaseEntity
{
    public long HeaderId { get; set; }
    public InventoryCountHeader Header { get; set; } = null!;
    public Guid TaskCode { get; set; } = Guid.NewGuid();
    public string TaskNo { get; set; } = string.Empty;
    public long WarehouseId { get; set; }
    public long LocationId { get; set; }
    public int RouteSequence { get; set; }
    public int CountRound { get; set; } = 1;
    public InventoryCountTaskStatus Status { get; set; } = InventoryCountTaskStatus.Ready;
    public long? AssignedUserId { get; set; }
    public long? AssignedTeamId { get; set; }
    public long? PreviousTaskId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public long? AssignedByUserId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? CompletedByUserId { get; set; }
    public bool LocationBarcodeConfirmed { get; set; }
    public DateTime? LocationConfirmedAtUtc { get; set; }
    public int LineCount { get; set; }
    public int CountedLineCount { get; set; }
    public int VarianceLineCount { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<InventoryCountLine> Lines { get; set; } = [];
}

public sealed class InventoryCountLine : BaseEntity
{
    public long HeaderId { get; set; }
    public InventoryCountHeader Header { get; set; } = null!;
    public long TaskId { get; set; }
    public InventoryCountTask Task { get; set; } = null!;
    public int SequenceNo { get; set; }
    public int CountRound { get; set; } = 1;
    public long WarehouseId { get; set; }
    public long LocationId { get; set; }
    public long StockId { get; set; }
    public long? YapCodeId { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public string LotNo { get; set; } = string.Empty;
    public string SerialNo { get; set; } = string.Empty;
    public string StockStatus { get; set; } = "Available";
    public decimal SnapshotQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal VarianceQuantity { get; set; }
    public decimal VariancePercentage { get; set; }
    public bool IsUnexpectedStock { get; set; }
    public bool IsZeroConfirmed { get; set; }
    public bool IsWithinTolerance { get; set; }
    public InventoryCountLineStatus Status { get; set; } = InventoryCountLineStatus.Pending;
    public DateTime? FirstCountedAtUtc { get; set; }
    public DateTime? LastCountedAtUtc { get; set; }
    public long? LastCountedByUserId { get; set; }
    public string? DifferenceReasonCode { get; set; }
    public string? ReviewNote { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<InventoryCountEntry> Entries { get; set; } = [];
}

public sealed class InventoryCountEntry : BaseEntity
{
    public long HeaderId { get; set; }
    public long TaskId { get; set; }
    public long LineId { get; set; }
    public InventoryCountLine Line { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public int CountRound { get; set; }
    public InventoryCountEntryType EntryType { get; set; }
    public decimal Quantity { get; set; }
    public string? Barcode { get; set; }
    public string? DeviceCode { get; set; }
    public string? SessionCode { get; set; }
    public long EnteredByUserId { get; set; }
    public DateTime EnteredAtUtc { get; set; }
    public string? Note { get; set; }
}

public sealed class InventoryCountScanEvent : BaseEntity
{
    public long HeaderId { get; set; }
    public long TaskId { get; set; }
    public long? LineId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public bool IsAccepted { get; set; }
    public string ResultCode { get; set; } = string.Empty;
    public string? ResultDetail { get; set; }
    public string? DeviceCode { get; set; }
    public long ScannedByUserId { get; set; }
    public DateTime ScannedAtUtc { get; set; }
}

public sealed class InventoryCountReview : BaseEntity
{
    public long HeaderId { get; set; }
    public long? TaskId { get; set; }
    public long? LineId { get; set; }
    public InventoryCountReviewAction Action { get; set; }
    public decimal? PreviousQuantity { get; set; }
    public decimal? ApprovedQuantity { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? Note { get; set; }
    public long ReviewedByUserId { get; set; }
    public DateTime ReviewedAtUtc { get; set; }
}

public sealed class InventoryCountAdjustment : BaseEntity
{
    public long HeaderId { get; set; }
    public long LineId { get; set; }
    public long StockMovementOperationId { get; set; }
    public decimal QuantityDelta { get; set; }
    public string Status { get; set; } = "Posted";
    public DateTime PostedAtUtc { get; set; }
    public long PostedByUserId { get; set; }
}

public sealed class InventoryCountPolicy : BaseEntity
{
    public long? WarehouseId { get; set; }
    public InventoryCountMode DefaultCountMode { get; set; } = InventoryCountMode.Blind;
    public InventoryCountMovementPolicy DefaultMovementPolicy { get; set; } = InventoryCountMovementPolicy.SnapshotWithMovementReconciliation;
    public decimal QuantityTolerance { get; set; }
    public decimal PercentageTolerance { get; set; }
    public int MaxCountAttempts { get; set; } = 2;
    public bool RequireIndependentRecount { get; set; } = true;
    public bool AllowUnexpectedStock { get; set; } = true;
    public bool AutoApproveWithinTolerance { get; set; } = true;
    public bool RequireDifferenceReason { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}
