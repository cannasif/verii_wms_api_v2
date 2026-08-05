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
    public void Line_serial_can_be_partially_moved_when_quantity_per_serial_is_disabled()
    {
        var policy=Policy(SerialQuantityRule.OneSerialPerLine);

        StockTrackingPolicyGuard.ValidateSerialMovementQuantity(policy,1,10,"DTG-1");
        StockTrackingPolicyGuard.ValidateSerialMovementQuantity(policy,10,10,"DTG-1");
    }

    private static EffectiveStockTrackingPolicy Policy(SerialQuantityRule rule)=>new(
        1,"STK-001",null,StockTrackingType.Serial,true,rule,false,false,false,false,null,
        true,"Stock",1,1,"TEST");
}
