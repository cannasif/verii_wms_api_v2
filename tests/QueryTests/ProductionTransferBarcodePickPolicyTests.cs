using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using Xunit;

namespace QueryTests;

public sealed class ProductionTransferBarcodePickPolicyTests
{
    [Fact]
    public void Quantity_per_serial_accepts_only_one_base_unit()
    {
        var quantity = ProductionTransferBarcodePickPolicy.CalculateQuantity(
            Policy(SerialQuantityRule.OneSerialPerBaseUnit), 9, 0, 9, 9, true);

        Assert.Equal(1, quantity);
    }

    [Fact]
    public void Pallet_serial_accepts_label_quantity_instead_of_forcing_one()
    {
        var quantity = ProductionTransferBarcodePickPolicy.CalculateQuantity(
            Policy(SerialQuantityRule.OneSerialPerLine), 9, 0, 9, 9, true);

        Assert.Equal(9, quantity);
    }

    [Fact]
    public void Quantity_bound_label_cannot_exceed_its_remaining_capacity()
    {
        var quantity = ProductionTransferBarcodePickPolicy.CalculateQuantity(
            Policy(SerialQuantityRule.OneSerialPerLine), 9, 7, 8, 9, true);

        Assert.Equal(2, quantity);
    }

    [Fact]
    public void Product_alias_scan_defaults_to_one_without_consuming_a_global_label_capacity()
    {
        var quantity = ProductionTransferBarcodePickPolicy.CalculateQuantity(
            Policy(SerialQuantityRule.NotApplicable, requireSerial: false), null, 0, 10, 10, false);

        Assert.Equal(1, quantity);
    }

    [Fact]
    public void Planned_serial_tracking_caps_pallet_barcode_to_the_ordered_serial_quantity()
    {
        var quantity = ProductionTransferBarcodePickPolicy.CalculateQuantity(
            Policy(SerialQuantityRule.OneSerialPerLine), 41, 0, 5, 41, true, 1);

        Assert.Equal(1, quantity);
    }

    private static EffectiveStockTrackingPolicy Policy(
        SerialQuantityRule rule,
        bool requireSerial = true) => new(
        1, "STK-001", null,
        requireSerial ? StockTrackingType.Serial : StockTrackingType.None,
        requireSerial, rule, false, false, false, false, null,
        true, "Stock", 1, 1, "TEST");
}
