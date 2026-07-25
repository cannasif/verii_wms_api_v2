using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Domain;

public enum WarehouseInboundExecutionMode
{
    Manual = 1,
    BarcodeScan = 2,
    PreGeneratedLabel = 3,
    SupplierLabel = 4,
    Import = 5
}

public enum WarehouseInboundExecutionStatus
{
    Posted = 1,
    Reversed = 2
}

/// <summary>
/// Immutable business evidence for one physical receiving command. Header/line totals are projections of these records.
/// </summary>
public sealed class WarehouseInboundExecution : BaseEntity
{
    public long GrHeaderId { get; set; }
    public WarehouseInboundHeader Header { get; set; } = null!;
    public long? GrTaskId { get; set; }
    public WarehouseInboundTask? Task { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string ExecutionNo { get; set; } = string.Empty;
    public WarehouseInboundExecutionMode Mode { get; set; } = WarehouseInboundExecutionMode.Manual;
    public WarehouseInboundExecutionStatus Status { get; set; } = WarehouseInboundExecutionStatus.Posted;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public long? StockMovementOperationId { get; set; }
    public string? DeviceId { get; set; }
    public string? Description { get; set; }
    public long? ReversalOfExecutionId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<WarehouseInboundExecutionLine> Lines { get; set; } = [];
}

public sealed class WarehouseInboundExecutionLine : BaseEntity
{
    public long GrExecutionId { get; set; }
    public WarehouseInboundExecution Execution { get; set; } = null!;
    public long GrLineId { get; set; }
    public WarehouseInboundLine Line { get; set; } = null!;
    public int LineNo { get; set; }
    public long StockId { get; set; }
    public long? YapCodeId { get; set; }
    public decimal Quantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public long? SerialNumberRuleId { get; set; }
    public int? SerialNumberRuleVersion { get; set; }
    public string? SerialNumberRuleCodeSnapshot { get; set; }
    public string? SerialMaskSnapshot { get; set; }
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public string? ScannedBarcode { get; set; }
    public long WarehouseId { get; set; }
    public long LocationId { get; set; }
    public string StockStatus { get; set; } = "Available";
    public long? WarehouseInboundLabelId { get; set; }
    public long? QualityInspectionLineId { get; set; }
}
