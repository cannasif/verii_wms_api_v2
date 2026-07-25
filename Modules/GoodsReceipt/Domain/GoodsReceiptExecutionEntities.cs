using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Domain;

public enum GoodsReceiptExecutionMode
{
    Manual = 1,
    BarcodeScan = 2,
    PreGeneratedLabel = 3,
    SupplierLabel = 4,
    Import = 5
}

public enum GoodsReceiptExecutionStatus
{
    Posted = 1,
    Reversed = 2
}

/// <summary>
/// Immutable business evidence for one physical receiving command. Header/line totals are projections of these records.
/// </summary>
public sealed class GoodsReceiptExecution : BaseEntity
{
    public long GrHeaderId { get; set; }
    public GoodsReceiptHeader Header { get; set; } = null!;
    public long? GrTaskId { get; set; }
    public GoodsReceiptTask? Task { get; set; }
    public Guid IdempotencyKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string ExecutionNo { get; set; } = string.Empty;
    public GoodsReceiptExecutionMode Mode { get; set; } = GoodsReceiptExecutionMode.Manual;
    public GoodsReceiptExecutionStatus Status { get; set; } = GoodsReceiptExecutionStatus.Posted;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public long? StockMovementOperationId { get; set; }
    public string? DeviceId { get; set; }
    public string? Description { get; set; }
    public long? ReversalOfExecutionId { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GoodsReceiptExecutionLine> Lines { get; set; } = [];
}

public sealed class GoodsReceiptExecutionLine : BaseEntity
{
    public long GrExecutionId { get; set; }
    public GoodsReceiptExecution Execution { get; set; } = null!;
    public long GrLineId { get; set; }
    public GoodsReceiptLine Line { get; set; } = null!;
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
    public long? GoodsReceiptLabelId { get; set; }
    public long? QualityInspectionLineId { get; set; }
}
