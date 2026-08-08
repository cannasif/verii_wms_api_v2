using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Kkd.Domain;

public enum KkdEntitlementPhaseType { Initial = 1, AfterMonths = 2, Recurring = 3 }
public enum KkdPeriodType { Day = 1, Month = 2, Year = 3 }
public enum KkdDistributionStatus { Draft = 1, Validated = 2, OutboundCreated = 3, Completed = 4, Cancelled = 5, Failed = 6 }
public enum KkdEntitlementSourceType { Matrix = 1, ManualOverride = 2, OpenOrderExcess = 3 }
public enum KkdExcessApprovalStatus { NotRequired = 1, Pending = 2, Approved = 3, Rejected = 4 }
public enum KkdRequestSourceType { Wms = 1, Windbox = 2, Netsis = 3, Manual = 4 }
public enum KkdRequestPriority { Low = 1, Normal = 2, High = 3, Urgent = 4 }
public enum KkdRequestStatus
{
    Open = 1,
    AwaitingStockSelection = 2,
    ReadyToPrepare = 3,
    InPreparation = 4,
    ReadyForDelivery = 5,
    PartiallyDelivered = 6,
    Completed = 7,
    Cancelled = 8
}
public enum KkdRequestLineStatus
{
    AwaitingStockSelection = 1,
    ReadyToPrepare = 2,
    InPreparation = 3,
    ReadyForDelivery = 4,
    PartiallyDelivered = 5,
    Completed = 6,
    Cancelled = 7
}

public sealed class KkdPolicy : BaseEntity
{
    public string PolicyKey { get; set; } = "DEFAULT";
    public bool EnableMaterialRequestOrderFlow { get; set; } = true;
    public bool RequireOpenOrder { get; set; } = true;
    public bool AllowOpenOrderExcess { get; set; } = true;
    public bool AllowMultipleOrdersPerDistribution { get; set; } = true;
    public bool RequireEmployeeUserLink { get; set; }
    public bool AllowFutureDatedDistribution { get; set; }
    public bool RequireManagerApprovalForExcess { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class KkdDepartment : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdRole> Roles { get; set; } = [];
    public ICollection<KkdEmployee> Employees { get; set; } = [];
}

public sealed class KkdRole : BaseEntity
{
    public long? DepartmentId { get; set; }
    public KkdDepartment? Department { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdEmployee> Employees { get; set; } = [];
}

public sealed class KkdEmployee : BaseEntity
{
    public long? UserId { get; set; }
    public long CustomerId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public long DepartmentId { get; set; }
    public KkdDepartment Department { get; set; } = null!;
    public long RoleId { get; set; }
    public KkdRole Role { get; set; } = null!;
    public string QrCode { get; set; } = string.Empty;
    public DateOnly EmploymentStartDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastSyncDate { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdEmployeeEntitlementOverride> Overrides { get; set; } = [];
}

public sealed class KkdEntitlementMatrix : BaseEntity
{
    public long CustomerId { get; set; }
    public long DepartmentId { get; set; }
    public KkdDepartment Department { get; set; } = null!;
    public long RoleId { get; set; }
    public KkdRole Role { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdEntitlementRule> Rules { get; set; } = [];
}

public sealed class KkdEntitlementRule : BaseEntity
{
    public long MatrixId { get; set; }
    public KkdEntitlementMatrix Matrix { get; set; } = null!;
    public string GroupCode { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public long? StockId { get; set; }
    public string? StockCodeSnapshot { get; set; }
    public string? StockNameSnapshot { get; set; }
    public string? StandardCode { get; set; }
    public string? StandardName { get; set; }
    public int? AnnualIssueCount { get; set; }
    public decimal? AnnualQuantity { get; set; }
    public decimal? MaxCarryQuantity { get; set; }
    public bool AllowBulkIssue { get; set; } = true;
    public bool IsMandatory { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdEntitlementPhase> Phases { get; set; } = [];
}

public sealed class KkdEntitlementPhase : BaseEntity
{
    public long RuleId { get; set; }
    public KkdEntitlementRule Rule { get; set; } = null!;
    public KkdEntitlementPhaseType PhaseType { get; set; }
    public int OffsetMonths { get; set; }
    public decimal Quantity { get; set; }
    public bool AllowBulkIssue { get; set; } = true;
    public int? FrequencyDays { get; set; }
    public decimal? QuantityPerFrequency { get; set; }
    public KkdPeriodType? PeriodType { get; set; }
    public int? PeriodInterval { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
}

public sealed class KkdEmployeeEntitlementOverride : BaseEntity
{
    public long EmployeeId { get; set; }
    public KkdEmployee Employee { get; set; } = null!;
    public long? RuleId { get; set; }
    public KkdEntitlementRule? Rule { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long ApprovedByUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class KkdEmployeeStockPreference : BaseEntity
{
    public long EmployeeId { get; set; }
    public KkdEmployee Employee { get; set; } = null!;
    public string GroupCode { get; set; } = string.Empty;
    public long StockId { get; set; }
    public DateTimeOffset LastSelectedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class KkdRequest : BaseEntity
{
    public Guid CorrelationId { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public long EmployeeId { get; set; }
    public KkdEmployee Employee { get; set; } = null!;
    public long CustomerId { get; set; }
    public long? WarehouseId { get; set; }
    public long? AssignedUserId { get; set; }
    public KkdRequestSourceType SourceType { get; set; } = KkdRequestSourceType.Wms;
    public string? ExternalRequestNo { get; set; }
    public KkdRequestPriority Priority { get; set; } = KkdRequestPriority.Normal;
    public KkdRequestStatus Status { get; set; } = KkdRequestStatus.Open;
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? NeededAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? ReadyAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdRequestLine> Lines { get; set; } = [];
}

public sealed class KkdRequestLine : BaseEntity
{
    public long RequestId { get; set; }
    public KkdRequest Request { get; set; } = null!;
    public int LineNo { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public long? StockId { get; set; }
    public string? StockCodeSnapshot { get; set; }
    public string? StockNameSnapshot { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public decimal RequestedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public KkdRequestLineStatus Status { get; set; } = KkdRequestLineStatus.AwaitingStockSelection;
    public string? ExternalOrderNo { get; set; }
    public string? ExternalOrderLineId { get; set; }
    public long? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? ResolutionReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdRequestLineResolution> Resolutions { get; set; } = [];
}

public sealed class KkdRequestLineResolution : BaseEntity
{
    public Guid IdempotencyKey { get; set; }
    public long RequestLineId { get; set; }
    public KkdRequestLine RequestLine { get; set; } = null!;
    public long? PreviousStockId { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset ResolvedAtUtc { get; set; }
}

public sealed class KkdDistribution : BaseEntity
{
    public Guid CorrelationId { get; set; }
    public long EmployeeId { get; set; }
    public KkdEmployee Employee { get; set; } = null!;
    public long CustomerId { get; set; }
    public long WarehouseId { get; set; }
    public long DocumentSeriesId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public KkdDistributionStatus Status { get; set; } = KkdDistributionStatus.Draft;
    public long? WarehouseOutboundId { get; set; }
    public long? KkdRequestId { get; set; }
    public KkdRequest? KkdRequest { get; set; }
    public KkdExcessApprovalStatus ExcessApprovalStatus { get; set; } = KkdExcessApprovalStatus.NotRequired;
    public string? ExcessApprovalReason { get; set; }
    public long? ExcessApprovedBy { get; set; }
    public DateTimeOffset? ExcessApprovedAtUtc { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdDistributionLine> Lines { get; set; } = [];
}

public sealed class KkdDistributionLine : BaseEntity
{
    public long DistributionId { get; set; }
    public KkdDistribution Distribution { get; set; } = null!;
    public int LineNo { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntitledQuantity { get; set; }
    public decimal ExcessQuantity { get; set; }
    public long SourceLocationId { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public string? OpenOrderNo { get; set; }
    public string? OpenOrderLineId { get; set; }
    public long? KkdRequestLineId { get; set; }
    public KkdRequestLine? KkdRequestLine { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<KkdDistributionEntitlementAllocation> EntitlementAllocations { get; set; } = [];
    public ICollection<KkdEntitlementConsumption> Consumptions { get; set; } = [];
}

public sealed class KkdDistributionEntitlementAllocation : BaseEntity
{
    public long DistributionLineId { get; set; }
    public KkdDistributionLine DistributionLine { get; set; } = null!;
    public KkdEntitlementSourceType SourceType { get; set; }
    public long SourceId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
}

public sealed class KkdEntitlementConsumption : BaseEntity
{
    public long EmployeeId { get; set; }
    public long DistributionId { get; set; }
    public long DistributionLineId { get; set; }
    public KkdDistributionLine DistributionLine { get; set; } = null!;
    public long StockId { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public KkdEntitlementSourceType SourceType { get; set; }
    public long? MatrixId { get; set; }
    public long? RuleId { get; set; }
    public long? PhaseId { get; set; }
    public long? OverrideId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset ConsumedAtUtc { get; set; }
    public bool IsReversal { get; set; }
    public long? ReversesConsumptionId { get; set; }
}

public sealed class KkdValidationLog : BaseEntity
{
    public Guid CorrelationId { get; set; }
    public long? EmployeeId { get; set; }
    public long? StockId { get; set; }
    public string? GroupCode { get; set; }
    public long? WarehouseId { get; set; }
    public decimal AttemptedQuantity { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? DeviceInfo { get; set; }
}
