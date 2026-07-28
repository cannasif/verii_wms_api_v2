using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelVehicleAcceptancePolicyTests
{
    [Fact]
    public void Unaccepted_selected_plate_does_not_conflict_when_sibling_from_same_plan_used_another_vehicle()
    {
        var selectedPlate = ExpectedPlate(planId: 100);

        Assert.False(SteelVehicleAcceptanceService.HasSelectedPlateConflict([selectedPlate]));
    }

    [Fact]
    public void Already_accepted_selected_plate_conflicts()
    {
        var selectedPlate = ExpectedPlate(planId: 100);
        selectedPlate.VehicleAcceptanceId = 17;

        Assert.True(SteelVehicleAcceptanceService.HasSelectedPlateConflict([selectedPlate]));
    }

    [Theory]
    [InlineData(SteelArrivalStatus.Arrived, SteelInspectionStatus.Pending, SteelReceiptConversionStatus.NotCreated)]
    [InlineData(SteelArrivalStatus.Expected, SteelInspectionStatus.Approved, SteelReceiptConversionStatus.NotCreated)]
    [InlineData(SteelArrivalStatus.Expected, SteelInspectionStatus.Pending, SteelReceiptConversionStatus.Created)]
    public void Processed_selected_plate_conflicts(
        SteelArrivalStatus arrivalStatus,
        SteelInspectionStatus inspectionStatus,
        SteelReceiptConversionStatus conversionStatus)
    {
        var selectedPlate = ExpectedPlate(planId: 100);
        selectedPlate.ArrivalStatus = arrivalStatus;
        selectedPlate.InspectionStatus = inspectionStatus;
        selectedPlate.ConversionStatus = conversionStatus;

        Assert.True(SteelVehicleAcceptanceService.HasSelectedPlateConflict([selectedPlate]));
    }

    private static SteelReceiptPlanLine ExpectedPlate(long planId) => new()
    {
        BranchCode = "0",
        PlanId = planId,
        DCode = "SAC-2026-000001",
        ExternalLineKey = "ROW-1",
        StockId = 1,
        StockCodeSnapshot = "SAC-STOK",
        SupplierSerialNo = "LEVHA-001",
        UnitCode = "ADET",
        ExpectedQuantity = 1,
        TargetWarehouseId = 1,
        ReceivingLocationId = 1,
        ArrivalStatus = SteelArrivalStatus.Expected,
        InspectionStatus = SteelInspectionStatus.Pending,
        ConversionStatus = SteelReceiptConversionStatus.NotCreated
    };
}
