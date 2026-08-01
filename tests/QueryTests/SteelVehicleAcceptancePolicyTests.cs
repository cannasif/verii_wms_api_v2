using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Api;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
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

    [Fact]
    public void Unknown_plate_status_keeps_vehicle_and_acceptance_partial_until_resolved()
    {
        Assert.Equal(
            SteelVehicleAcceptanceStatus.PartiallyIdentified,
            SteelVehicleAcceptanceService.ResolveAcceptanceStatus(hasUnknownPlates: true));
        Assert.Equal(
            SteelVehicleAcceptanceStatus.Completed,
            SteelVehicleAcceptanceService.ResolveAcceptanceStatus(hasUnknownPlates: false));
    }

    [Fact]
    public void Known_slot_without_receiving_location_passes_request_policy()
    {
        var request = new CompleteSteelVehicleAcceptanceRequest(
            Guid.NewGuid(),
            new SaveVehicleCheckInRequest(
                Id: null,
                RowVersion: null,
                BranchCode: "0",
                PlateNo: "34 TEST 36",
                TrailerPlateNo: null,
                DriverFirstName: null,
                DriverLastName: null,
                DriverPhone: null,
                CarrierName: null,
                SteelSheetCount: 1,
                CustomerId: 10,
                Note: null),
            [
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Known,
                    PlanLineId: 100,
                    ReceivingLocationId: null,
                    RowVersion: Convert.ToBase64String([1, 2, 3]),
                    Note: null)
            ],
            Note: null);

        SteelVehicleAcceptanceService.ValidateRequest(request, [], []);
    }

    [Fact]
    public void Mixed_known_and_unknown_slots_pass_request_policy()
    {
        var request = new CompleteSteelVehicleAcceptanceRequest(
            Guid.NewGuid(),
            new SaveVehicleCheckInRequest(
                Id: null,
                RowVersion: null,
                BranchCode: "0",
                PlateNo: "34 TEST 34",
                TrailerPlateNo: null,
                DriverFirstName: null,
                DriverLastName: null,
                DriverPhone: null,
                CarrierName: null,
                SteelSheetCount: 2,
                CustomerId: 10,
                Note: null),
            [
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Known,
                    PlanLineId: 100,
                    ReceivingLocationId: 1,
                    RowVersion: Convert.ToBase64String([1, 2, 3]),
                    Note: null),
                new AcceptSteelPlateSlot(
                    SteelPlateIdentityStatus.Unknown,
                    PlanLineId: null,
                    ReceivingLocationId: null,
                    RowVersion: null,
                    Note: null)
            ],
            Note: null);

        SteelVehicleAcceptanceService.ValidateRequest(request, [], []);
    }

    [Fact]
    public void Receipt_conversion_is_blocked_for_the_whole_vehicle_when_unknown_slots_remain()
    {
        var exception = Assert.ThrowsAny<Exception>(
            () => SteelReceiptService.EnsureVehicleHasNoUnknownPlates(2));

        Assert.Contains("2 adet bilinmeyen levha", exception.Message);
    }

    [Fact]
    public void Incremental_acceptance_uses_existing_plus_new_slot_count()
    {
        SteelVehicleAcceptanceService.EnsureTargetSlotCount(
            existingSlotCount: 2,
            newSlotCount: 1,
            targetSteelSheetCount: 3);

        Assert.ThrowsAny<Exception>(() =>
            SteelVehicleAcceptanceService.EnsureTargetSlotCount(2, 1, 2));
    }

    [Fact]
    public void Null_customer_does_not_clear_existing_vehicle_supplier()
    {
        Assert.Equal(42, VehicleCheckInService.ResolveCustomerId(null, 42));
        Assert.Equal(51, VehicleCheckInService.ResolveCustomerId(51, 42));
    }

    [Fact]
    public void Unknown_plate_slots_do_not_require_photos_by_policy()
    {
        var request = new CompleteSteelVehicleAcceptanceRequest(
            Guid.NewGuid(),
            new SaveVehicleCheckInRequest(
                null, null, "0", "34 TEST 35", null, null, null, null, null,
                1, 10, null),
            [new AcceptSteelPlateSlot(
                SteelPlateIdentityStatus.Unknown, null, null, null, null)],
            null);

        SteelVehicleAcceptanceService.ValidateRequest(request, [], []);
    }

    [Fact]
    public void Multipart_request_json_accepts_string_unknown_identity_status()
    {
        const string json =
            """
            {
              "idempotencyKey": "1e191caf-37c2-4eb7-851f-87281a780743",
              "vehicle": {
                "branchCode": "0",
                "plateNo": "54 FRT 654",
                "steelSheetCount": 1
              },
              "slots": [
                { "identityStatus": "Unknown" }
              ]
            }
            """;

        var request = SteelReceiptsController.DeserializeVehicleAcceptanceRequest(json);

        Assert.Equal(
            SteelPlateIdentityStatus.Unknown,
            Assert.Single(request.Slots).IdentityStatus);
        Assert.Null(request.Vehicle.CustomerId);
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
