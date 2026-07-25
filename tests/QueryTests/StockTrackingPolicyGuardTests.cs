using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class StockTrackingPolicyGuardTests
{
    [Fact]
    public void One_serial_per_base_unit_accepts_exact_unique_distribution()
    {
        StockTrackingPolicyGuard.Validate(
            Policy(StockTrackingType.Serial, serial: true, serialRule: SerialQuantityRule.OneSerialPerBaseUnit),
            2,
            StockTrackingType.Serial,
            [Capture("SN-001"), Capture("SN-002")],
            requireCompleteCapture: true);
    }

    [Fact]
    public void One_serial_per_base_unit_rejects_missing_serial_count()
    {
        var exception = Assert.Throws<StockTrackingPolicyViolationException>(() =>
            StockTrackingPolicyGuard.Validate(
                Policy(StockTrackingType.Serial, serial: true, serialRule: SerialQuantityRule.OneSerialPerBaseUnit),
                2,
                StockTrackingType.Serial,
                [Capture("SN-001")],
                requireCompleteCapture: true));
        Assert.Contains("eşleşmelidir", exception.Message);
    }

    [Fact]
    public void Mandatory_lot_and_expiration_are_enforced()
    {
        var policy = Policy(StockTrackingType.Lot, lot: true);
        policy = policy with { RequireExpirationDate = true };
        Assert.Throws<StockTrackingPolicyViolationException>(() =>
            StockTrackingPolicyGuard.Validate(
                policy,
                5,
                StockTrackingType.Lot,
                [new StockTrackingCapture(5, null, null, null, null)],
                requireCompleteCapture: true));
    }

    [Fact]
    public void Client_cannot_downgrade_effective_tracking_type()
    {
        Assert.Throws<StockTrackingPolicyViolationException>(() =>
            StockTrackingPolicyGuard.Validate(
                Policy(StockTrackingType.Serial, serial: true, serialRule: SerialQuantityRule.OneSerialPerLine),
                1,
                StockTrackingType.None,
                [],
                requireCompleteCapture: true));
    }

    [Fact]
    public void Client_cannot_enable_tracking_when_effective_policy_is_default_none()
    {
        var defaultPolicy = Policy(StockTrackingType.None) with
        {
            HasPolicy = false,
            Source = "Default",
            PolicyId = null,
            PolicyVersion = null,
            PolicyCode = null
        };

        Assert.Throws<StockTrackingPolicyViolationException>(() =>
            StockTrackingPolicyGuard.Validate(
                defaultPolicy,
                1,
                StockTrackingType.Serial,
                [Capture("UNAUTHORIZED-SERIAL")],
                requireCompleteCapture: false));
    }

    [Fact]
    public void Minimum_remaining_shelf_life_is_enforced()
    {
        var policy = Policy(StockTrackingType.Lot, lot: true) with
        {
            RequireExpirationDate = true,
            MinimumRemainingShelfLifeDays = 30
        };
        var tooSoon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
        Assert.Throws<StockTrackingPolicyViolationException>(() =>
            StockTrackingPolicyGuard.Validate(
                policy,
                1,
                StockTrackingType.Lot,
                [new StockTrackingCapture(1, "LOT-01", null, null, tooSoon)],
                requireCompleteCapture: true));
    }

    private static EffectiveStockTrackingPolicy Policy(
        StockTrackingType type,
        bool lot = false,
        bool serial = false,
        SerialQuantityRule serialRule = SerialQuantityRule.NotApplicable) =>
        new(1, "TEST-STOCK", "TEST", type, serial, serialRule, lot, false, false, null,
            true, "Stock", 1, 1, "TEST-POLICY");

    private static StockTrackingCapture Capture(string serial) => new(1, null, serial, null, null);
}
