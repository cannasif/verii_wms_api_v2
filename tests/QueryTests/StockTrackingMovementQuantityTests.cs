using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using Xunit;

namespace QueryTests;

public sealed class StockTrackingMovementQuantityTests
{
    [Fact]
    public void Unit_serial_requires_one_unit_and_a_clean_source_balance()
    {
        var policy=Policy(SerialQuantityRule.OneSerialPerBaseUnit);

        StockTrackingPolicyGuard.ValidateSerialMovementQuantity(policy,1,1,"SER-001");

        Assert.Throws<StockTrackingPolicyViolationException>(()=>
            StockTrackingPolicyGuard.ValidateSerialMovementQuantity(policy,1,9,"SER-001"));
    }

    [Fact]
    public void Weighted_serial_cannot_be_partially_moved_to_another_location()
    {
        var policy=Policy(SerialQuantityRule.OneSerialPerLine);

        var error=Assert.Throws<StockTrackingPolicyViolationException>(()=>
            StockTrackingPolicyGuard.ValidateSerialMovementQuantity(policy,1,10,"DTG-1"));

        Assert.Contains("tamamını seçin",error.Message);
        StockTrackingPolicyGuard.ValidateSerialMovementQuantity(policy,10,10,"DTG-1");
    }

    private static EffectiveStockTrackingPolicy Policy(SerialQuantityRule rule)=>new(
        1,"STK-001",null,StockTrackingType.Serial,true,rule,false,false,false,false,null,
        true,"Stock",1,1,"TEST");
}
