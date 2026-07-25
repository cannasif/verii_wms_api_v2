using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Shipping.Application;

public interface IShipmentReservationService
{
    Task ReserveAsync(ShipmentHeader header, string key, long actor, CancellationToken ct);
    Task ConsumeAsync(ShipmentHeader header, IReadOnlyDictionary<long, ShipmentOperationLineRequest> lines, string key, long actor, CancellationToken ct);
    Task ReleaseAllAsync(ShipmentHeader header, string key, string reason, long actor, CancellationToken ct);
}

public sealed class ShipmentReservationService(IStockBalanceService balances) : IShipmentReservationService
{
    public async Task ReserveAsync(ShipmentHeader header, string key, long actor, CancellationToken ct)
    {
        var drafts = Build(header, reserve: true);
        if (drafts.Count == 0) return;
        await Post(header, key, StockReservationOperationTypes.Reserve, "Sevk stok rezervasyonu", drafts, ct);
        Apply(drafts, actor);
    }

    public async Task ConsumeAsync(ShipmentHeader header, IReadOnlyDictionary<long, ShipmentOperationLineRequest> lines,
        string key, long actor, CancellationToken ct)
    {
        if (header.ReservationPolicy == ShipmentReservationPolicy.None) return;
        var drafts = new List<(ShipmentLine Line, ShipmentTracking? Tracking, StockReservationLineRequest Request)>();
        foreach (var line in header.Lines.Where(x => lines.ContainsKey(x.Id)))
        {
            var item = lines[line.Id];
            if (line.ReservedQuantity < item.Quantity)
                throw AppException.Conflict($"{line.LineNo}. satırın rezervasyonu toplama miktarını karşılamıyor.");
            ShipmentTracking? tracking = null;
            if (line.Trackings.Count > 0)
            {
                tracking = line.Trackings.FirstOrDefault(x => Equal(x.LotNo, item.LotNo) && Equal(x.SerialNo, item.SerialNo)
                    && (x.SourceLocationId ?? line.DefaultSourceLocationId) == (item.SourceLocationId ?? x.SourceLocationId ?? line.DefaultSourceLocationId));
                if (tracking is null || tracking.ReservedQuantity < item.Quantity)
                    throw AppException.Conflict($"{line.LineNo}. satırın lot/seri rezervasyonu toplama isteğiyle eşleşmiyor.");
            }
            var locationId = item.SourceLocationId ?? tracking?.SourceLocationId ?? line.DefaultSourceLocationId
                ?? throw AppException.Conflict($"{line.LineNo}. satır için rezervasyon rafı bulunamadı.");
            drafts.Add((line, tracking, Row(header, line, locationId, item.LotNo, item.SerialNo, -item.Quantity)));
        }
        if (drafts.Count == 0) return;
        await Post(header, key, StockReservationOperationTypes.Consume, "Sevk toplamasında rezervasyon tüketimi", drafts, ct);
        Apply(drafts, actor);
    }

    public async Task ReleaseAllAsync(ShipmentHeader header, string key, string reason, long actor, CancellationToken ct)
    {
        var drafts = Build(header, reserve: false);
        if (drafts.Count == 0) return;
        await Post(header, key, StockReservationOperationTypes.Release, reason, drafts, ct);
        Apply(drafts, actor);
    }

    private static List<(ShipmentLine Line, ShipmentTracking? Tracking, StockReservationLineRequest Request)> Build(ShipmentHeader header, bool reserve)
    {
        var drafts = new List<(ShipmentLine, ShipmentTracking?, StockReservationLineRequest)>();
        foreach (var line in header.Lines)
        {
            if (line.Trackings.Count > 0)
            {
                foreach (var tracking in line.Trackings)
                {
                    var quantity = reserve ? tracking.PlannedQuantity - tracking.ReservedQuantity : -tracking.ReservedQuantity;
                    if (quantity == 0) continue;
                    var locationId = tracking.SourceLocationId ?? line.DefaultSourceLocationId
                        ?? throw AppException.Conflict($"{line.LineNo}. satır için rezervasyon rafı bulunamadı.");
                    drafts.Add((line, tracking, Row(header, line, locationId, tracking.LotNo, tracking.SerialNo, quantity)));
                }
            }
            else
            {
                var quantity = reserve ? line.RequestedQuantity - line.ReservedQuantity : -line.ReservedQuantity;
                if (quantity == 0) continue;
                var locationId = line.DefaultSourceLocationId
                    ?? throw AppException.Conflict($"{line.LineNo}. satır için rezervasyon rafı bulunamadı.");
                drafts.Add((line, null, Row(header, line, locationId, null, null, quantity)));
            }
        }
        return drafts;
    }

    private Task<StockReservationPostResult> Post(ShipmentHeader header, string key, string type, string reason,
        IReadOnlyCollection<(ShipmentLine Line, ShipmentTracking? Tracking, StockReservationLineRequest Request)> rows, CancellationToken ct) =>
        balances.PostReservationAsync(new(key, "Shipment", header.Id, header.DocumentNo, type, reason, rows.Select(x => x.Request).ToList()), ct);

    private static void Apply(IEnumerable<(ShipmentLine Line, ShipmentTracking? Tracking, StockReservationLineRequest Request)> rows, long actor)
    {
        foreach (var row in rows)
        {
            row.Line.ReservedQuantity += row.Request.QuantityDelta;
            row.Line.Status = row.Line.ReservedQuantity > 0 ? ShipmentLineStatus.Reserved : row.Line.Status;
            row.Line.UpdatedBy = actor;
            row.Line.UpdatedDate = DateTime.UtcNow;
            if (row.Tracking is null) continue;
            row.Tracking.ReservedQuantity += row.Request.QuantityDelta;
            row.Tracking.UpdatedBy = actor;
            row.Tracking.UpdatedDate = DateTime.UtcNow;
        }
    }

    private static StockReservationLineRequest Row(ShipmentHeader header, ShipmentLine line, long locationId, string? lot, string? serial, decimal delta) =>
        new(line.Id, header.SourceWarehouseId, locationId, line.StockId, line.YapCodeId, line.UnitCode, lot, serial, "Available", delta);
    private static bool Equal(string? left, string? right) =>
        string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
