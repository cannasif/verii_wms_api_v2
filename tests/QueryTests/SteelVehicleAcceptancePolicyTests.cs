using verii_wms_api_v2.Modules.SteelReceipt.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelVehicleAcceptancePolicyTests
{
    [Fact]
    public void Plan_header_link_without_completed_acceptance_does_not_conflict()
    {
        var conflict = SteelVehicleAcceptanceService.HasConflictingAcceptedVehicle(
            [],
            currentVehicleId: 42);

        Assert.False(conflict);
    }

    [Fact]
    public void Acceptance_completed_by_same_vehicle_does_not_conflict()
    {
        var conflict = SteelVehicleAcceptanceService.HasConflictingAcceptedVehicle(
            [42],
            currentVehicleId: 42);

        Assert.False(conflict);
    }

    [Fact]
    public void Acceptance_completed_by_different_vehicle_conflicts()
    {
        var conflict = SteelVehicleAcceptanceService.HasConflictingAcceptedVehicle(
            [17],
            currentVehicleId: 42);

        Assert.True(conflict);
    }
}
