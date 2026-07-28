using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelReceiptConversionPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Direct_receipt_rejects_task_assignment(bool assignAll)
    {
        var assignedUsers = assignAll ? null : new long[] { 42 };

        var exception = Assert.Throws<AppException>(() =>
            SteelReceiptService.ValidateConversionMode(
                SteelReceiptConversionMode.Direct,
                assignAll,
                assignedUsers));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("kullanıcı ataması", exception.Message);
    }

    [Fact]
    public void Task_conversion_accepts_assignment_contract()
    {
        SteelReceiptService.ValidateConversionMode(
            SteelReceiptConversionMode.Task,
            assignToAllActiveUsers: false,
            assignedUserIds: [42]);
    }

    [Fact]
    public void Steel_plate_maps_to_one_weighted_serial_capture()
    {
        var line = new SteelReceiptPlanLine
        {
            StockId = 7,
            YapCodeId = 8,
            ApprovedQuantity = 2_480.75m,
            UnitCode = "KG",
            SupplierSerialNo = "LEVHA-0007",
            DCode = "SAC-2026-000007",
            TargetWarehouseId = 11,
            ReceivingLocationId = 12
        };

        var mapped = SteelReceiptService.BuildManualGoodsReceiptLineForConvert(line);

        Assert.Equal("LEVHA-0007", mapped.SerialNo);
        Assert.Equal(2_480.75m, mapped.Quantity);
        Assert.Equal("KG", mapped.UnitCode);
    }

    [Fact]
    public void Unknown_conversion_mode_is_rejected()
    {
        Assert.Throws<AppException>(() =>
            SteelReceiptService.ValidateConversionMode(
                (SteelReceiptConversionMode)99,
                assignToAllActiveUsers: false,
                assignedUserIds: null));
    }

    [Fact]
    public void Exact_direct_retry_is_replayed_but_changed_document_is_not()
    {
        var key = Guid.NewGuid();
        var date = new DateOnly(2026, 7, 28);
        var receipt = new GoodsReceiptHeader
        {
            CorrelationId = key,
            InitiationMode = GoodsReceiptInitiationMode.DirectReceipt,
            WaybillNo = "123456789012345",
            WaybillDate = date
        };

        Assert.True(SteelReceiptService.IsCompatibleReplay(
            receipt,key,SteelReceiptConversionMode.Direct,
            "123456789012345",null,date));
        Assert.False(SteelReceiptService.IsCompatibleReplay(
            receipt,key,SteelReceiptConversionMode.Task,
            "123456789012345",null,date));
        Assert.False(SteelReceiptService.IsCompatibleReplay(
            receipt,key,SteelReceiptConversionMode.Direct,
            "999999999999999",null,date));
    }
}
