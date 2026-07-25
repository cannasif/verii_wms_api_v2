using verii_wms_api_v2.Modules.GoodsReceipt.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record GoodsReceiptRoutingLineRequest(long GoodsReceiptLineId, decimal Quantity, long? SourceLocationId);

public sealed record CreateGoodsReceiptTransferRequest(
    Guid IdempotencyKey,
    long DocumentSeriesId,
    long TargetWarehouseId,
    long? TargetReceivingLocationId,
    long? TargetPutawayLocationId,
    byte Priority,
    string? Description,
    IReadOnlyList<GoodsReceiptRoutingLineRequest> Lines);

public sealed record CreateGoodsReceiptOutboundRequest(
    Guid IdempotencyKey,
    long DocumentSeriesId,
    long CustomerId,
    long? StagingLocationId,
    long? LoadingLocationId,
    byte Priority,
    string? Description,
    IReadOnlyList<GoodsReceiptRoutingLineRequest> Lines);

public sealed record GoodsReceiptRoutingResult(
    long RoutingBatchId,
    GoodsReceiptRouteType RouteType,
    long TargetDocumentId,
    string TargetDocumentNo,
    decimal RoutedQuantity,
    bool Replayed);

public interface IGoodsReceiptRoutingService
{
    Task<IReadOnlyDictionary<long, decimal>> GetActiveAllocatedQuantitiesAsync(IReadOnlyCollection<long> goodsReceiptLineIds, CancellationToken cancellationToken = default);
    Task<GoodsReceiptRoutingResult> CreateTransferAsync(long goodsReceiptId, CreateGoodsReceiptTransferRequest request, long actor, CancellationToken cancellationToken = default);
    Task<GoodsReceiptRoutingResult> CreateOutboundAsync(long goodsReceiptId, CreateGoodsReceiptOutboundRequest request, long actor, CancellationToken cancellationToken = default);
}
