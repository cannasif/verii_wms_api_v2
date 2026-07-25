using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Quality.Domain;

public enum QualityInspectionMode { NoCheck = 1, QuickCheck = 2, InspectionRequired = 3 }
public enum QualityFailAction { Quarantine = 1, Reject = 2, ReturnToSupplier = 3, ManagerApproval = 4 }
public enum QualitySamplingMode { All = 1, Percentage = 2, FixedQuantity = 3, EveryNthHandlingUnit = 4 }
public enum QualityInspectionStatus { Pending = 1, InProgress = 2, PartiallyDecided = 3, Passed = 4, Failed = 5, Quarantined = 6, Released = 7, Cancelled = 8 }
public enum QualityDecision { Pending = 1, Accepted = 2, Rejected = 3, Quarantined = 4, Returned = 5, Hold = 6 }

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
    public long? DefaultQuarantineLocationId { get; set; }
    public long? DefaultRejectLocationId { get; set; }
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
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal QuarantineQuantity { get; set; }
    public QualityDecision Decision { get; set; } = QualityDecision.Pending;
    public string? ReasonCode { get; set; }
    public string? ReasonNote { get; set; }
    public long? DecisionBy { get; set; }
    public DateTimeOffset? DecisionAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
