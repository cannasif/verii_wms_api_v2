using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Location.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

internal static class GoodsReceiptLocationPolicy
{
    internal static bool IsAllowed(
        GoodsReceiptLocationSelectionPolicy policy,
        WarehouseLocation location,
        long warehouseId)
    {
        if (!location.IsActive || location.WarehouseId != warehouseId)
            return false;

        return policy == GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation
            || location.LocationType is LocationTypes.Receiving or LocationTypes.Staging;
    }

    internal static bool IsAllowedForReceiptLine(
        GoodsReceiptLocationSelectionPolicy policy,
        WarehouseLocation location,
        long warehouseId,
        bool requiresQuality,
        bool blockPutawayUntilQualityDecision)
    {
        if (!location.IsActive || location.WarehouseId != warehouseId)
            return false;

        if (!requiresQuality || !blockPutawayUntilQualityDecision)
            return true;

        return IsAllowed(policy, location, warehouseId);
    }
}
