using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Production.Domain;

public enum ProductionPlanType
{
    MakeToStock = 1,
    MakeToOrder = 2,
    Rework = 3,
    Disassembly = 4
}

public enum ProductionExecutionMode
{
    Serial = 1,
    Parallel = 2
}

public enum ProductionPlanStatus
{
    Draft = 1,
    Released = 2,
    InProgress = 3,
    PartiallyCompleted = 4,
    Completed = 5,
    Cancelled = 6
}

public enum ProductionOrderStatus
{
    Draft = 1,
    Released = 2,
    InProgress = 3,
    PartiallyCompleted = 4,
    Completed = 5,
    Cancelled = 6,
    Blocked = 7
}

public enum ProductionMaterialIssueMode
{
    Manual = 1,
    Backflush = 2
}

public enum ProductionDependencyType
{
    FinishToStart = 1,
    StartToStart = 2,
    FinishToFinish = 3
}

public enum ProductionOrderSourceType
{
    NetsisErpFunctions = 1,
    WmsIntegrationTables = 2,
    ErpAndWms = 3
}

public enum ProductionSourceOrderStatus
{
    Draft = 1,
    Ready = 2,
    Released = 3,
    OnHold = 4,
    Closed = 5,
    Cancelled = 6
}

/// <summary>
/// Versioned inbound contract written by an approved planning system such as Windbox.
/// It is intentionally separated from operational WMS production orders.
/// </summary>
public sealed class ProductionSourceWorkOrder : BaseEntity
{
    public string SourceSystemCode { get; set; } = "WINDBOX";
    public string ExternalKey { get; set; } = string.Empty;
    public string WorkOrderNumber { get; set; } = string.Empty;
    public int RevisionNumber { get; set; } = 1;
    public ProductionSourceOrderStatus Status { get; set; } = ProductionSourceOrderStatus.Draft;
    public string ProductCode { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? ConfigurationCode { get; set; }
    public decimal PlannedQuantity { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public int SourceWarehouseCode { get; set; }
    public int TargetWarehouseCode { get; set; }
    public DateTime? WorkOrderDate { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string? ProjectCode { get; set; }
    public DateTimeOffset SourceUpdatedAtUtc { get; set; }
    public string? PayloadHash { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ProductionSourceRecipeLine> RecipeLines { get; set; } = [];
}

public sealed class ProductionSourceRecipeLine : BaseEntity
{
    public long ProductionSourceWorkOrderId { get; set; }
    public ProductionSourceWorkOrder WorkOrder { get; set; } = null!;
    public int LineNumber { get; set; }
    public int OperationNumber { get; set; }
    public string ComponentStockCode { get; set; } = string.Empty;
    public string? ComponentStockName { get; set; }
    public string? ComponentConfigurationCode { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public decimal RecipeQuantity { get; set; }
    public decimal VariableWasteQuantity { get; set; }
    public decimal FixedWasteQuantity { get; set; }
    public decimal TotalRequiredQuantity { get; set; }
    public bool IsMandatory { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ProductionHeader : BaseEntity
{
    public long DocumentSeriesId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public Guid CorrelationId { get; set; }
    public ProductionPlanType PlanType { get; set; }
    public ProductionExecutionMode ExecutionMode { get; set; }
    public ProductionPlanStatus Status { get; set; } = ProductionPlanStatus.Draft;
    public byte Priority { get; set; } = 3;
    public long? CustomerId { get; set; }
    public string? CustomerCodeSnapshot { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public DateTimeOffset? PlannedStartAtUtc { get; set; }
    public DateTimeOffset? PlannedEndAtUtc { get; set; }
    public DateTimeOffset? ActualStartAtUtc { get; set; }
    public DateTimeOffset? ActualEndAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public long? ReleasedBy { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ProductionOrder> Orders { get; set; } = [];
    public ICollection<ProductionOrderDependency> Dependencies { get; set; } = [];
}

public sealed class ProductionOrder : BaseEntity
{
    public long ProductionHeaderId { get; set; }
    public ProductionHeader Header { get; set; } = null!;
    public int LineNo { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string? ExternalOrderNo { get; set; }
    public string? ExternalSourceSystemCode { get; set; }
    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Draft;
    public int SequenceNo { get; set; }
    public int? ParallelGroupNo { get; set; }
    public string? BomReference { get; set; }
    public string? RoutingReference { get; set; }
    public string? WorkCenterCode { get; set; }
    public long ProducedStockId { get; set; }
    public string ProducedStockCodeSnapshot { get; set; } = string.Empty;
    public string? ProducedStockNameSnapshot { get; set; }
    public long? ProducedYapCodeId { get; set; }
    public string? ProducedYapCodeSnapshot { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal PlannedQuantity { get; set; }
    public decimal CompletedQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public long SourceWarehouseId { get; set; }
    public long TargetWarehouseId { get; set; }
    public bool RequireMaterialTransferBeforeStart { get; set; } = true;
    public DateTimeOffset? PlannedStartAtUtc { get; set; }
    public DateTimeOffset? PlannedEndAtUtc { get; set; }
    public DateTimeOffset? ActualStartAtUtc { get; set; }
    public DateTimeOffset? ActualEndAtUtc { get; set; }
    public string? BlockedReason { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<ProductionMaterialRequirement> Materials { get; set; } = [];
    public ICollection<ProductionOutputExpectation> Outputs { get; set; } = [];
    public ICollection<ProductionOrderAssignment> Assignments { get; set; } = [];
}

public sealed class ProductionMaterialRequirement : BaseEntity
{
    public long ProductionOrderId { get; set; }
    public ProductionOrder Order { get; set; } = null!;
    public int LineNo { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public ProductionMaterialIssueMode IssueMode { get; set; }
    public bool IsMandatory { get; set; } = true;
    public long SourceWarehouseId { get; set; }
    public long? PreferredSourceLocationId { get; set; }
    public StockTrackingType TrackingType { get; set; }
}

public sealed class ProductionOutputExpectation : BaseEntity
{
    public long ProductionOrderId { get; set; }
    public ProductionOrder Order { get; set; } = null!;
    public int LineNo { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string? StockNameSnapshot { get; set; }
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal PlannedQuantity { get; set; }
    public decimal ProducedQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public long TargetWarehouseId { get; set; }
    public long? PreferredTargetLocationId { get; set; }
    public StockTrackingType TrackingType { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class ProductionOrderAssignment : BaseEntity
{
    public long ProductionOrderId { get; set; }
    public ProductionOrder Order { get; set; } = null!;
    public long UserId { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; }
    public long AssignedBy { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? Note { get; set; }
}

public sealed class ProductionOrderDependency : BaseEntity
{
    public long ProductionHeaderId { get; set; }
    public ProductionHeader Header { get; set; } = null!;
    public long PredecessorOrderId { get; set; }
    public ProductionOrder PredecessorOrder { get; set; } = null!;
    public long SuccessorOrderId { get; set; }
    public ProductionOrder SuccessorOrder { get; set; } = null!;
    public ProductionDependencyType DependencyType { get; set; }
    public int LagMinutes { get; set; }
    public bool RequireOutputAvailable { get; set; }
    public bool RequireTransferCompleted { get; set; }
}
