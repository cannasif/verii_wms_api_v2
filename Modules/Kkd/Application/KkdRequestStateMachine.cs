using verii_wms_api_v2.Modules.Kkd.Domain;

namespace verii_wms_api_v2.Modules.Kkd.Application;

internal static class KkdRequestStateMachine
{
    public static void Refresh(KkdRequest request, DateTimeOffset now)
    {
        foreach (var line in request.Lines.Where(x => x.Status != KkdRequestLineStatus.Cancelled))
        {
            var effective = line.RequestedQuantity - line.CancelledQuantity;
            line.Status = line.StockId is null ? KkdRequestLineStatus.AwaitingStockSelection
                : line.DeliveredQuantity >= effective ? KkdRequestLineStatus.Completed
                : line.DeliveredQuantity > 0 ? KkdRequestLineStatus.PartiallyDelivered
                : line.AllocatedQuantity > 0 ? KkdRequestLineStatus.InPreparation
                : KkdRequestLineStatus.ReadyToPrepare;
        }

        var active = request.Lines.Where(x => x.Status != KkdRequestLineStatus.Cancelled).ToArray();
        request.Status = active.Length == 0 ? KkdRequestStatus.Cancelled
            : active.All(x => x.Status == KkdRequestLineStatus.Completed) ? KkdRequestStatus.Completed
            : active.Any(x => x.Status == KkdRequestLineStatus.PartiallyDelivered || x.DeliveredQuantity > 0) ? KkdRequestStatus.PartiallyDelivered
            : active.Any(x => x.Status == KkdRequestLineStatus.ReadyForDelivery) ? KkdRequestStatus.ReadyForDelivery
            : active.Any(x => x.Status == KkdRequestLineStatus.InPreparation || x.AllocatedQuantity > 0) ? KkdRequestStatus.InPreparation
            : active.Any(x => x.StockId is null) ? KkdRequestStatus.AwaitingStockSelection
            : KkdRequestStatus.ReadyToPrepare;

        request.ReadyAtUtc = request.Status == KkdRequestStatus.ReadyToPrepare ? request.ReadyAtUtc ?? now : request.ReadyAtUtc;
        request.CompletedAtUtc = request.Status == KkdRequestStatus.Completed ? request.CompletedAtUtc ?? now : null;
    }
}
