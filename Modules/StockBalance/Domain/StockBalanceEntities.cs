using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.StockBalance.Domain;

public sealed class LocationStockBalance : BaseEntity
{
    public string DimensionKey { get; set; } = string.Empty;
    public long WarehouseId { get; set; }
    public long LocationId { get; set; }
    public long StockId { get; set; }
    public long? YapCodeId { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public string LotNo { get; set; } = string.Empty;
    public string SerialNo { get; set; } = string.Empty;
    public string StockStatus { get; set; } = "Available";
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public long LastMovementEntryId { get; set; }
    public DateTime LastTransactionDate { get; set; }
    public DateTime? LastReconciledAt { get; set; }
}

public sealed class WarehouseStockBalance : BaseEntity
{
    public string DimensionKey { get; set; } = string.Empty;
    public long WarehouseId { get; set; }
    public long StockId { get; set; }
    public long? YapCodeId { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public string StockStatus { get; set; } = "Available";
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public int DistinctLocationCount { get; set; }
    public int DistinctLotCount { get; set; }
    public int DistinctSerialCount { get; set; }
    public long LastMovementEntryId { get; set; }
    public DateTime LastTransactionDate { get; set; }
    public DateTime? LastReconciledAt { get; set; }
}

public sealed class StockBalanceProjectionState : BaseEntity
{
    public string ProjectionName { get; set; } = StockBalanceProjectionNames.Current;
    public long LastMovementEntryId { get; set; }
    public DateTime? LastProjectedAt { get; set; }
    public DateTime? LastReconciledAt { get; set; }
    public int LastMismatchCount { get; set; }
}

public sealed class StockReservationOperation : BaseEntity
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }
    public string? ReferenceNo { get; set; }
    public string OperationType { get; set; } = StockReservationOperationTypes.Reserve;
    public string? Reason { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public ICollection<StockReservationEntry> Entries { get; set; } = [];
}

public sealed class StockReservationEntry : BaseEntity
{
    public long OperationId { get; set; }
    public StockReservationOperation Operation { get; set; } = null!;
    public int LineNo { get; set; }
    public long ReferenceLineId { get; set; }
    public long WarehouseId { get; set; }
    public long LocationId { get; set; }
    public long StockId { get; set; }
    public long? YapCodeId { get; set; }
    public string UnitCode { get; set; } = "ADET";
    public string LotNo { get; set; } = string.Empty;
    public string SerialNo { get; set; } = string.Empty;
    public string StockStatus { get; set; } = "Available";
    public decimal QuantityDelta { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public static class StockReservationOperationTypes
{
    public const string Reserve = "Reserve";
    public const string Consume = "Consume";
    public const string Release = "Release";
    public static readonly string[] All = [Reserve, Consume, Release];
}

public static class StockBalanceProjectionNames
{
    public const string Current = "stock-balance-v1";
}
