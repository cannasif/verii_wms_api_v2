using verii_wms_api_v2.Modules.WarehouseOutbound.Application;

namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed class KkdWarehouseOutboundShipmentFinalizationHandler(
    IKkdDistributionCompletionService completion) : IWarehouseOutboundShipmentFinalizationHandler
{
    public async Task OnShippedAsync(long warehouseOutboundId, Guid idempotencyKey, long actor, CancellationToken ct = default)
    {
        await completion.CompleteByWarehouseOutboundAsync(warehouseOutboundId, idempotencyKey, actor, ct);
    }
}
