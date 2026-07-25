using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Domain;

public enum GoodsReceiptRouteType
{
    WarehouseTransfer = 1,
    WarehouseOutbound = 2
}

/// <summary>
/// A durable, idempotent link between one goods receipt and the downstream
/// warehouse document created from it.
/// </summary>
public sealed class GoodsReceiptRoutingBatch : BaseEntity
{
    public long GrHeaderId { get; set; }
    public GoodsReceiptHeader Header { get; set; } = null!;
    public GoodsReceiptRouteType RouteType { get; set; }
    public Guid CorrelationId { get; set; }
    public long TargetDocumentId { get; set; }
    public string TargetDocumentNo { get; set; } = string.Empty;
    public DateTimeOffset RoutedAtUtc { get; set; }
    public long RoutedBy { get; set; }
    public string? Description { get; set; }
    public ICollection<GoodsReceiptRoutingAllocation> Allocations { get; set; } = [];
}

/// <summary>
/// Immutable quantity allocation. Availability is calculated from allocations
/// whose target document is still active; history is retained when a target is cancelled.
/// </summary>
public sealed class GoodsReceiptRoutingAllocation : BaseEntity
{
    public long RoutingBatchId { get; set; }
    public GoodsReceiptRoutingBatch RoutingBatch { get; set; } = null!;
    public long GrLineId { get; set; }
    public GoodsReceiptLine GoodsReceiptLine { get; set; } = null!;
    public long TargetDocumentLineId { get; set; }
    public decimal Quantity { get; set; }
}
