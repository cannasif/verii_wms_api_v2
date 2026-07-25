using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Packing.Domain;

public enum PackagingMaterialType { Box=1, Pallet=2, Crate=3, Bag=4, Envelope=5, Drum=6, ReturnableContainer=7, Other=99 }
public enum PackingSessionStatus { Draft=1, InProgress=2, Packed=3, Released=4, Cancelled=5 }
public enum HandlingUnitStatus { Open=1, Closed=2, Released=3, Loaded=4, Shipped=5, Cancelled=6 }
public enum PackingSourceType { WarehouseOutbound=1, Shipment=2, WarehouseTransfer=3, Manual=99 }
public enum PackingClosePolicy { Manual=1, AutoWhenComplete=2 }
public enum PackingReleasePolicy { Manual=1, OnClose=2 }
public enum PackingPrintJobStatus { Pending=1, Processing=2, Completed=3, Failed=4, Cancelled=5 }

public sealed class PackagingMaterial : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PackagingMaterialType Type { get; set; }
    public decimal TareWeight { get; set; }
    public decimal? MaxNetWeight { get; set; }
    public decimal? MaxGrossWeight { get; set; }
    public decimal? InnerLength { get; set; }
    public decimal? InnerWidth { get; set; }
    public decimal? InnerHeight { get; set; }
    public decimal? MaxVolume { get; set; }
    public bool IsReturnable { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PackingStation : BaseEntity
{
    public long WarehouseId { get; set; }
    public long? LocationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ScaleDeviceCode { get; set; }
    public long? PrinterDefinitionId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PackingPolicy : BaseEntity
{
    public string PolicyKey { get; set; } = "DEFAULT";
    public bool RequirePacking { get; set; } = true;
    public bool AllowPartialPacking { get; set; } = true;
    public bool AllowMixedStock { get; set; }
    public bool AllowMixedLot { get; set; }
    public bool AllowMixedCustomer { get; set; }
    public bool RequireSerialLotScan { get; set; } = true;
    public bool RequireWeight { get; set; }
    public decimal WeightTolerancePercent { get; set; } = 5;
    public bool RequireDimensions { get; set; }
    public bool RequireSscc { get; set; } = true;
    public bool AutoGenerateSscc { get; set; } = true;
    public bool AutoPrintLabelOnClose { get; set; }
    public bool AllowReopen { get; set; } = true;
    public bool AllowRepack { get; set; } = true;
    public PackingClosePolicy ClosePolicy { get; set; } = PackingClosePolicy.Manual;
    public PackingReleasePolicy ReleasePolicy { get; set; } = PackingReleasePolicy.Manual;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PackagingSpecification : BaseEntity
{
    public long? StockId { get; set; }
    public string? StockGroupCode { get; set; }
    public long? CustomerId { get; set; }
    public long PackagingMaterialId { get; set; }
    public decimal? UnitsPerHandlingUnit { get; set; }
    public decimal? MaxNetWeight { get; set; }
    public decimal? MaxVolume { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public sealed class PackingSession : BaseEntity
{
    public string PackingNo { get; set; } = string.Empty;
    public PackingSourceType SourceType { get; set; }
    public long? SourceHeaderId { get; set; }
    public string? SourceDocumentNo { get; set; }
    public long WarehouseId { get; set; }
    public long PackingStationId { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerCodeSnapshot { get; set; }
    public PackingSessionStatus Status { get; set; } = PackingSessionStatus.Draft;
    public Guid IdempotencyKey { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public DateTimeOffset? ReleasedAtUtc { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<HandlingUnit> HandlingUnits { get; set; } = [];
}

public sealed class HandlingUnit : BaseEntity
{
    public long PackingSessionId { get; set; }
    public PackingSession Session { get; set; } = null!;
    public long? ParentHandlingUnitId { get; set; }
    public HandlingUnit? Parent { get; set; }
    public long PackagingMaterialId { get; set; }
    public string HandlingUnitNo { get; set; } = string.Empty;
    public string? Sscc { get; set; }
    public HandlingUnitStatus Status { get; set; } = HandlingUnitStatus.Open;
    public decimal TareWeight { get; set; }
    public decimal NetWeight { get; set; }
    public decimal? MeasuredGrossWeight { get; set; }
    public decimal GrossWeight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? Volume { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public long? ClosedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<HandlingUnit> Children { get; set; } = [];
    public ICollection<HandlingUnitLine> Lines { get; set; } = [];
}

public sealed class HandlingUnitLine : BaseEntity
{
    public long HandlingUnitId { get; set; }
    public HandlingUnit HandlingUnit { get; set; } = null!;
    public long SourceLineId { get; set; }
    public long StockId { get; set; }
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public long? YapCodeId { get; set; }
    public string? YapCodeSnapshot { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? LotNo { get; set; }
    public string? SerialNo { get; set; }
    public DateTimeOffset PackedAtUtc { get; set; }
    public long PackedBy { get; set; }
}

public sealed class PackingEvent : BaseEntity
{
    public long PackingSessionId { get; set; }
    public long? HandlingUnitId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid IdempotencyKey { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public long ActorId { get; set; }
}

public sealed class PackingPrintJob : BaseEntity
{
    public long HandlingUnitId { get; set; }
    public long PackingStationId { get; set; }
    public long? PrinterDefinitionId { get; set; }
    public PackingPrintJobStatus Status { get; set; } = PackingPrintJobStatus.Pending;
    public int Copies { get; set; } = 1;
    public string PayloadJson { get; set; } = string.Empty;
    public Guid IdempotencyKey { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? ProcessingStartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastError { get; set; }
}

public sealed class PackingScaleReading : BaseEntity
{
    public long PackingStationId { get; set; }
    public long? HandlingUnitId { get; set; }
    public string DeviceCode { get; set; } = string.Empty;
    public decimal GrossWeight { get; set; }
    public bool IsStable { get; set; }
    public Guid IdempotencyKey { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string? RawPayload { get; set; }
}
