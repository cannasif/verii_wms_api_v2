using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.StockMovement.Domain;

public sealed class StockMovementOperation : BaseEntity
{
    public Guid OperationCode { get; set; } = Guid.NewGuid();
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = StockMovementStatuses.Posted;
    public string? ReferenceType { get; set; }
    public string? ReferenceNo { get; set; }
    public long? ReferenceId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Reason { get; set; }
    public string? Description { get; set; }
    public long? ReversalOfOperationId { get; set; }
}

public sealed class StockMovementEntry : BaseEntity
{
    public long OperationId { get; set; }
    public int LineNo { get; set; }
    public long StockId { get; set; }
    public long? YapCodeId { get; set; }
    public long WarehouseId { get; set; }
    public long LocationId { get; set; }
    public decimal QuantityDelta { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public string StockStatus { get; set; } = "Available";
    public DateTime OccurredAt { get; set; }
}

public static class StockMovementTypes
{
    public const string Receipt = "Receipt";
    public const string Shipment = "Shipment";
    public const string Transfer = "Transfer";
    public const string AdjustmentIncrease = "AdjustmentIncrease";
    public const string AdjustmentDecrease = "AdjustmentDecrease";
    public const string CustomerReturn = "CustomerReturn";
    public const string SupplierReturn = "SupplierReturn";
    public const string Reversal = "Reversal";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { Receipt, Shipment, Transfer, AdjustmentIncrease, AdjustmentDecrease, CustomerReturn, SupplierReturn };
}

public static class StockMovementStatuses
{
    public const string Posted = "Posted";
    public const string Reversed = "Reversed";
}
