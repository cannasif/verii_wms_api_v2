using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

public interface IWarehouseTransferReservationService
{
    Task ReserveAsync(WarehouseTransferHeader header, string idempotencyKey, long actor, CancellationToken ct);
    Task ConsumeAsync(WarehouseTransferHeader header, IReadOnlyDictionary<long, WarehouseTransferOperationLineRequest> lines, string idempotencyKey, long actor, CancellationToken ct);
    Task ReleaseAllAsync(WarehouseTransferHeader header, string idempotencyKey, string reason, long actor, CancellationToken ct);
}

public sealed class WarehouseTransferReservationService(IStockBalanceService balances) : IWarehouseTransferReservationService
{
    public async Task ReserveAsync(WarehouseTransferHeader header, string idempotencyKey, long actor, CancellationToken ct)
    {
        var drafts = new List<(WarehouseTransferLine Line, WarehouseTransferTracking? Tracking, StockReservationLineRequest Request)>();
        foreach (var line in header.Lines)
        {
            if (line.Trackings.Count > 0)
            {
                foreach (var tracking in line.Trackings)
                {
                    // Planlanandan, hâlihazırda rezerve olanı VE zaten fiziksel olarak toplanmış
                    // (henüz iade edilmemiş) miktarı da düşmek gerekir — aksi halde bu fonksiyon
                    // ilk yayınlamadan sonra tekrar çağrıldığında (iade/rota yenileme sonrası),
                    // kısmen toplanmış bir satırın kalanından fazlasını rezerve etmeye çalışır.
                    var quantity = tracking.PlannedQuantity - tracking.ReservedQuantity - tracking.PickedQuantity;
                    if (quantity <= 0) continue;
                    var locationId = tracking.SourceLocationId ?? line.DefaultSourceLocationId
                        ?? throw AppException.Conflict($"{line.LineNo}. satır için rezervasyon rafı bulunamadı.");
                    drafts.Add((line, tracking, Row(line, locationId, tracking.LotNo, tracking.SerialNo, quantity)));
                }
            }
            else
            {
                var quantity = line.RequestedQuantity - line.ReservedQuantity - line.PickedQuantity;
                if (quantity <= 0) continue;
                var locationId = line.DefaultSourceLocationId
                    ?? throw AppException.Conflict($"{line.LineNo}. satır için rezervasyon rafı bulunamadı.");
                drafts.Add((line, null, Row(line, locationId, null, null, quantity)));
            }
        }
        if (drafts.Count == 0) return;
        await balances.PostReservationAsync(new(idempotencyKey, "WarehouseTransfer", header.Id, header.DocumentNo,
            StockReservationOperationTypes.Reserve, "Transfer stok rezervasyonu", drafts.Select(x => x.Request).ToList()), ct);
        foreach (var draft in drafts)
        {
            draft.Line.ReservedQuantity += draft.Request.QuantityDelta;
            if (draft.Tracking is not null)
            {
                draft.Tracking.ReservedQuantity += draft.Request.QuantityDelta;
                draft.Tracking.Status = WarehouseTransferTrackingStatus.Reserved;
                draft.Tracking.UpdatedBy = actor;
                draft.Tracking.UpdatedDate = DateTime.UtcNow;
            }
            draft.Line.Status = WarehouseTransferLineStatus.Reserved;
            draft.Line.UpdatedBy = actor;
            draft.Line.UpdatedDate = DateTime.UtcNow;
        }
    }

    public async Task ConsumeAsync(WarehouseTransferHeader header, IReadOnlyDictionary<long, WarehouseTransferOperationLineRequest> lines,
        string idempotencyKey, long actor, CancellationToken ct)
    {
        if (header.ReservationPolicy == WarehouseTransferReservationPolicy.None) return;
        var drafts = new List<(WarehouseTransferLine Line, WarehouseTransferTracking? Tracking, StockReservationLineRequest Request)>();
        foreach (var line in header.Lines.Where(x => lines.ContainsKey(x.Id)))
        {
            var item = lines[line.Id];
            if (line.ReservedQuantity < item.Quantity)
                throw AppException.Conflict(
                    $"{line.LineNo}. satırın rezervasyonu toplama miktarını karşılamıyor. " +
                    $"İstenen:{line.RequestedQuantity} Toplanan:{line.PickedQuantity} Rezerve:{line.ReservedQuantity} " +
                    $"İstenenToplama:{item.Quantity} Durum:{line.Status}");
            WarehouseTransferTracking? tracking = null;
            if (line.Trackings.Count > 0)
            {
                tracking = line.Trackings.FirstOrDefault(x =>
                    Equal(x.LotNo, item.LotNo) && Equal(x.SerialNo, item.SerialNo)
                    && (x.SourceLocationId ?? line.DefaultSourceLocationId) == (item.SourceLocationId ?? x.SourceLocationId ?? line.DefaultSourceLocationId));
                if (tracking is null || tracking.ReservedQuantity < item.Quantity)
                    throw AppException.Conflict($"{line.LineNo}. satırın lot/seri rezervasyonu toplama isteğiyle eşleşmiyor.");
            }
            var locationId = item.SourceLocationId ?? tracking?.SourceLocationId ?? line.DefaultSourceLocationId
                ?? throw AppException.Conflict($"{line.LineNo}. satır için rezervasyon rafı bulunamadı.");
            drafts.Add((line, tracking, Row(line, locationId, item.LotNo, item.SerialNo, -item.Quantity)));
        }
        if (drafts.Count == 0) return;
        await balances.PostReservationAsync(new(idempotencyKey, "WarehouseTransfer", header.Id, header.DocumentNo,
            StockReservationOperationTypes.Consume, "Transfer toplamasında rezervasyon tüketimi", drafts.Select(x => x.Request).ToList()), ct);
        foreach (var draft in drafts)
        {
            draft.Line.ReservedQuantity += draft.Request.QuantityDelta;
            if (draft.Tracking is not null)
            {
                draft.Tracking.ReservedQuantity += draft.Request.QuantityDelta;
                draft.Tracking.UpdatedBy = actor;
                draft.Tracking.UpdatedDate = DateTime.UtcNow;
            }
        }
    }

    public async Task ReleaseAllAsync(WarehouseTransferHeader header, string idempotencyKey, string reason, long actor, CancellationToken ct)
    {
        var drafts = new List<(WarehouseTransferLine Line, WarehouseTransferTracking? Tracking, StockReservationLineRequest Request)>();
        foreach (var line in header.Lines.Where(x => x.ReservedQuantity > 0))
        {
            if (line.Trackings.Any(x => x.ReservedQuantity > 0))
            {
                foreach (var tracking in line.Trackings.Where(x => x.ReservedQuantity > 0))
                {
                    var locationId = tracking.SourceLocationId ?? line.DefaultSourceLocationId
                        ?? throw AppException.Conflict($"{line.LineNo}. satır için rezervasyon rafı bulunamadı.");
                    drafts.Add((line, tracking, Row(line, locationId, tracking.LotNo, tracking.SerialNo, -tracking.ReservedQuantity)));
                }
            }
            else
            {
                var locationId = line.DefaultSourceLocationId
                    ?? throw AppException.Conflict($"{line.LineNo}. satır için rezervasyon rafı bulunamadı.");
                drafts.Add((line, null, Row(line, locationId, null, null, -line.ReservedQuantity)));
            }
        }
        if (drafts.Count == 0) return;
        await balances.PostReservationAsync(new(idempotencyKey, "WarehouseTransfer", header.Id, header.DocumentNo,
            StockReservationOperationTypes.Release, reason, drafts.Select(x => x.Request).ToList()), ct);
        foreach (var draft in drafts)
        {
            draft.Line.ReservedQuantity += draft.Request.QuantityDelta;
            if (draft.Tracking is not null)
            {
                draft.Tracking.ReservedQuantity += draft.Request.QuantityDelta;
                draft.Tracking.Status = WarehouseTransferTrackingStatus.Cancelled;
                draft.Tracking.UpdatedBy = actor;
                draft.Tracking.UpdatedDate = DateTime.UtcNow;
            }
        }
    }

    private static StockReservationLineRequest Row(WarehouseTransferLine line, long locationId, string? lotNo, string? serialNo, decimal delta) =>
        new(line.Id, line.SourceWarehouseId, locationId, line.StockId, line.YapCodeId, line.UnitCode, lotNo, serialNo, "Available", delta);
    private static bool Equal(string? left, string? right) =>
        string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
