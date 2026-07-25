using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseOutbound.Application;

internal enum WarehouseOutboundOperationPhase
{
    Pick,
    Pack,
    Load,
    Ship
}

internal static class WarehouseOutboundOperationGuard
{
    public static void ValidateTrackingDimension(
        WarehouseOutboundHeader header,
        WarehouseOutboundLine line,
        WarehouseOutboundOperationLineRequest request,
        WarehouseOutboundOperationPhase phase)
    {
        var lotNo = Normalize(request.LotNo);
        var serialNo = Normalize(request.SerialNo);
        var handlingUnitNo = Normalize(request.HandlingUnitNo);

        if (line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial)
        {
            if (serialNo is null)
                throw AppException.BadRequest($"{line.LineNo}. satır için seri numarası zorunludur.");
            if (request.Quantity != 1)
                throw AppException.BadRequest($"{line.LineNo}. satırda serili stok miktarı 1 olmalıdır.");
        }

        if (line.TrackingType is StockTrackingType.Lot or StockTrackingType.LotAndSerial && lotNo is null)
            throw AppException.BadRequest($"{line.LineNo}. satır için lot numarası zorunludur.");

        if (line.RequireHandlingUnit && handlingUnitNo is null)
            throw AppException.BadRequest($"{line.LineNo}. satır için palet/kasa numarası zorunludur.");

        var hasDimension = lotNo is not null || serialNo is not null || handlingUnitNo is not null;
        var tracking = line.Trackings.FirstOrDefault(x =>
            Equal(x.LotNo, lotNo)
            && Equal(x.SerialNo, serialNo)
            && Equal(x.HandlingUnitNo, handlingUnitNo));

        if (phase == WarehouseOutboundOperationPhase.Pick && line.Trackings.Count > 0 && tracking is null)
            throw AppException.Conflict($"{line.LineNo}. satırın seri/lot/palet bilgisi planlanan takip kaydıyla eşleşmiyor.");

        if (phase != WarehouseOutboundOperationPhase.Pick && hasDimension && tracking is null)
            throw AppException.Conflict($"{line.LineNo}. satırın seri/lot/palet bilgisi önceki operasyonla eşleşmiyor.");

        if (tracking is null) return;

        if (phase == WarehouseOutboundOperationPhase.Pick
            && request.SourceLocationId.HasValue
            && tracking.SourceLocationId.HasValue
            && request.SourceLocationId != tracking.SourceLocationId)
            throw AppException.Conflict($"{line.LineNo}. satırın kaynak rafı planlanan seri/lot rafıyla eşleşmiyor.");

        var available = phase switch
        {
            WarehouseOutboundOperationPhase.Pick => tracking.PlannedQuantity - tracking.PickedQuantity,
            WarehouseOutboundOperationPhase.Pack => tracking.PickedQuantity - tracking.PackedQuantity,
            WarehouseOutboundOperationPhase.Load => (header.PackingPolicy == WarehouseOutboundPackingPolicy.Required
                ? tracking.PackedQuantity : tracking.PickedQuantity) - tracking.LoadedQuantity,
            WarehouseOutboundOperationPhase.Ship => (header.RequireLoadingConfirmation
                ? tracking.LoadedQuantity
                : header.PackingPolicy == WarehouseOutboundPackingPolicy.Required
                    ? tracking.PackedQuantity
                    : tracking.PickedQuantity) - tracking.ShippedQuantity,
            _ => 0
        };

        if (request.Quantity > available)
            throw AppException.Conflict(
                $"{line.LineNo}. satırın seçilen seri/lot/palet boyutunda kullanılabilir miktarı {available}, istenen {request.Quantity}.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Equal(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
