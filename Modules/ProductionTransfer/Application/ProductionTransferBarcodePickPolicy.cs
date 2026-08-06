using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferBarcodePickPolicy
{
    internal static decimal CalculateQuantity(
        EffectiveStockTrackingPolicy policy,
        decimal? barcodeQuantity,
        decimal alreadyAcceptedFromBarcode,
        decimal remainingLineQuantity,
        decimal sourceAvailableQuantity,
        bool quantityBoundBarcode)
    {
        if (remainingLineQuantity <= 0 || sourceAvailableQuantity <= 0) return 0;

        var barcodeCapacity = quantityBoundBarcode
            ? Math.Max(0, (barcodeQuantity ?? 0) - alreadyAcceptedFromBarcode)
            : barcodeQuantity.GetValueOrDefault(1);
        if (barcodeCapacity <= 0)
            throw AppException.Conflict("Bu lojistik etiketin miktarı bu transferde daha önce tamamen toplandı.");

        var quantity = policy.RequireSerial
            && policy.SerialQuantityRule == SerialQuantityRule.OneSerialPerBaseUnit
                ? 1m
                : barcodeCapacity;
        return Math.Min(quantity, Math.Min(remainingLineQuantity, sourceAvailableQuantity));
    }

    internal static bool IsQuantityBoundSource(string source) =>
        source is "GoodsReceiptLabel" or "WarehouseInboundLabel";
}
