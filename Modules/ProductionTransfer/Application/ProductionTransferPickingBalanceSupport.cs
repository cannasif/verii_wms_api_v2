using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferPickingBalanceSupport
{
    internal static async Task<IReadOnlyList<WarehouseBarcodeBalanceCandidate>> FindPickBalanceCandidatesAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        long locationId,
        string? lot,
        string? serial,
        CancellationToken ct)
    {
        var balances = await uow.Repository<LocationStockBalance>().Query()
            .Where(x => x.WarehouseId == header.SourceWarehouseId
                && x.LocationId == locationId
                && x.StockId == line.StockId
                && x.StockStatus == "Available"
                && x.Quantity > 0)
            .ToListAsync(ct);
        if (balances.Count == 0) return [];

        var location = await uow.Repository<WarehouseLocation>().Query()
            .Where(x => x.Id == locationId)
            .Select(x => new { x.Code, x.Name })
            .SingleOrDefaultAsync(ct);
        if (location is null) return [];

        return balances
            .Where(x => MatchesYapCode(line.YapCodeId, x.YapCodeId)
                && string.Equals(x.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase)
                && SameTrackingValue(x.LotNo, lot)
                && SameTrackingValue(x.SerialNo, serial))
            .Select(x =>
            {
                var pickable = ResolvePickableQuantity(line, locationId, x);
                if (pickable <= 0) return null;
                return new WarehouseBarcodeBalanceCandidate(
                    x.Id,
                    x.WarehouseId,
                    x.LocationId,
                    location.Code,
                    location.Name,
                    x.StockId,
                    x.YapCodeId,
                    x.UnitCode,
                    EmptyToNull(x.LotNo),
                    EmptyToNull(x.SerialNo),
                    x.StockStatus,
                    pickable);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();
    }

    internal static bool HasPickableBalanceAtLocation(
        WarehouseTransferLine line,
        long locationId,
        IReadOnlyList<WarehouseBarcodeBalanceCandidate> candidates) =>
        candidates.Any(x => x.LocationId == locationId && x.AvailableQuantity > 0)
        || ResolvePickableQuantity(line, locationId, reservedOnly: true) > 0;

    internal static decimal ResolvePickableQuantity(
        WarehouseTransferLine line,
        long locationId,
        LocationStockBalance? balance = null,
        bool reservedOnly = false)
    {
        if (balance is not null)
        {
            var reserved = ResolveLineReservedQuantity(line, locationId, balance.LotNo, balance.SerialNo);
            if (reserved > 0)
                return Math.Min(balance.Quantity, reserved);

            if (balance.AvailableQuantity > 0) return balance.AvailableQuantity;
            return 0;
        }

        if (!reservedOnly) return 0;
        return ResolveLineReservedQuantity(line, locationId, null, null);
    }

    private static decimal ResolveLineReservedQuantity(
        WarehouseTransferLine line,
        long locationId,
        string? lot,
        string? serial)
    {
        if (line.Trackings.Count > 0)
        {
            return line.Trackings
                .Where(x => x.ReservedQuantity > 0
                    && (x.SourceLocationId ?? line.DefaultSourceLocationId) == locationId
                    && SameTrackingValue(x.LotNo, lot)
                    && SameTrackingValue(x.SerialNo, serial))
                .Sum(x => x.ReservedQuantity);
        }

        if (line.DefaultSourceLocationId != locationId || line.ReservedQuantity <= 0)
            return 0;
        if (EmptyToNull(lot) is not null || EmptyToNull(serial) is not null) return 0;
        return line.ReservedQuantity;
    }

    private static bool MatchesYapCode(long? expected, long? actual) => expected == actual;

    private static bool SameTrackingValue(string? left, string? right) =>
        string.Equals(
            EmptyToNull(left),
            EmptyToNull(right),
            StringComparison.OrdinalIgnoreCase);

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
