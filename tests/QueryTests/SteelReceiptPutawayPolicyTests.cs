using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelReceiptPutawayPolicyTests
{
    [Fact]
    public void Putaway_uses_execution_lot_not_heat_number()
    {
        var line = Plate(heatNumber: "H-99", receivingLocationId: 12);
        var execution = Execution(lotNo: null, serialNo: "LEVHA-0007", locationId: 12);
        var balances = new[]
        {
            Balance(warehouseId: 11, locationId: 12, lotNo: "", serialNo: "LEVHA-0007", available: 1)
        };

        var source = SteelReceiptService.ResolvePutawayInventorySource(line, execution, balances, 11, 74);

        Assert.Null(source.LotNo);
        Assert.Equal("LEVHA-0007", source.SerialNo);
        Assert.Equal(12, source.LocationId);
        Assert.True(source.RequiresTransfer);
    }

    [Fact]
    public void Putaway_does_not_match_heat_number_as_lot()
    {
        var line = Plate(heatNumber: "H-99", receivingLocationId: 12);
        var execution = Execution(lotNo: null, serialNo: "LEVHA-0007", locationId: 12);
        var balances = new[]
        {
            Balance(warehouseId: 11, locationId: 12, lotNo: "H-99", serialNo: "LEVHA-0007", available: 1)
        };

        var exception = Assert.Throws<AppException>(() =>
            SteelReceiptService.ResolvePutawayInventorySource(line, execution, balances, 11, 74));

        Assert.Equal(409, exception.StatusCode);
        Assert.Contains("Kullanılabilir: 0", exception.Message);
    }

    [Fact]
    public void Putaway_follows_current_available_location_after_quality_move()
    {
        var line = Plate(heatNumber: "H-99", receivingLocationId: 12);
        var execution = Execution(lotNo: null, serialNo: "levha-0007", locationId: 12);
        var balances = new[]
        {
            Balance(warehouseId: 11, locationId: 43, lotNo: "", serialNo: "LEVHA-0007", available: 1)
        };

        var source = SteelReceiptService.ResolvePutawayInventorySource(line, execution, balances, 11, 74);

        Assert.Equal(43, source.LocationId);
        Assert.Equal("levha-0007", source.SerialNo);
        Assert.True(source.RequiresTransfer);
    }

    [Fact]
    public void Putaway_skips_transfer_when_already_on_destination()
    {
        var line = Plate(heatNumber: null, receivingLocationId: 12);
        var execution = Execution(lotNo: null, serialNo: "LEVHA-0007", locationId: 12);
        var balances = new[]
        {
            Balance(warehouseId: 11, locationId: 74, lotNo: "", serialNo: "LEVHA-0007", available: 1)
        };

        var source = SteelReceiptService.ResolvePutawayInventorySource(line, execution, balances, 11, 74);

        Assert.Equal(74, source.LocationId);
        Assert.False(source.RequiresTransfer);
    }

    [Fact]
    public void Putaway_serial_falls_back_to_supplier_serial()
    {
        var line = Plate(heatNumber: null, receivingLocationId: 12);
        var execution = Execution(lotNo: null, serialNo: null, locationId: 12);

        Assert.Equal("LEVHA-0007", SteelReceiptService.ResolvePutawaySerial(line, execution.SerialNo));
    }

    private static SteelReceiptPlanLine Plate(string? heatNumber, long receivingLocationId) => new()
    {
        StockId = 7,
        YapCodeId = 8,
        ApprovedQuantity = 1,
        UnitCode = "ADET",
        SupplierSerialNo = "LEVHA-0007",
        DCode = "SAC-2026-000007",
        HeatNumber = heatNumber,
        TargetWarehouseId = 11,
        ReceivingLocationId = receivingLocationId
    };

    private static SteelPutawayExecutionSnapshot Execution(string? lotNo, string? serialNo, long locationId) =>
        new(7, 8, "ADET", lotNo, serialNo, 11, locationId, "Available", 99);

    private static SteelPutawayBalanceCandidate Balance(
        long warehouseId, long locationId, string? lotNo, string? serialNo, decimal available) =>
        new(warehouseId, locationId, 7, 8, "ADET", lotNo, serialNo, available);
}
