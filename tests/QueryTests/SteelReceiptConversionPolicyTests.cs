using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelReceiptConversionPolicyTests
{
    [Theory]
    [InlineData("IRS202600000001")]
    [InlineData("GIB2026AB000001")]
    public void Source_waybill_is_always_treated_as_electronic(
        string source)
    {
        var result = SteelReceiptService.ResolveConversionDocumentReference(null, null, source);

        Assert.Null(result.WaybillNo);
        Assert.Equal(source, result.ElectronicWaybillNo);
    }

    [Fact]
    public void Explicit_conversion_waybill_overrides_source_reference()
    {
        var result = SteelReceiptService.ResolveConversionDocumentReference(
            null,
            "NEW2026AB000001",
            "IRS202600000001");

        Assert.Null(result.WaybillNo);
        Assert.Equal("NEW2026AB000001", result.ElectronicWaybillNo);
    }

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
            HeatNumber = "H-99",
            TargetWarehouseId = 11,
            ReceivingLocationId = 12
        };

        var mapped = SteelReceiptService.BuildManualGoodsReceiptLineForConvert(line);

        Assert.Equal("LEVHA-0007", mapped.SerialNo);
        Assert.Null(mapped.LotNo);
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
            "123456789012345",null,date,GoodsReceiptTradeType.Domestic,null));
        Assert.False(SteelReceiptService.IsCompatibleReplay(
            receipt,key,SteelReceiptConversionMode.Task,
            "123456789012345",null,date,GoodsReceiptTradeType.Domestic,null));
        Assert.False(SteelReceiptService.IsCompatibleReplay(
            receipt,key,SteelReceiptConversionMode.Direct,
            "999999999999999",null,date,GoodsReceiptTradeType.Domestic,null));
    }

    [Fact]
    public void Foreign_receipt_requires_and_normalizes_import_file_number()
    {
        var normalized = GoodsReceiptOperationsService.ValidateTradeClassification(
            GoodsReceiptTradeType.Foreign,
            "  IMP-2026-001  ");

        Assert.Equal("IMP-2026-001", normalized);
        Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateTradeClassification(
                GoodsReceiptTradeType.Foreign,
                null));
        Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateTradeClassification(
                GoodsReceiptTradeType.Domestic,
                "IMP-2026-001"));
    }

    [Fact]
    public void Foreign_receipt_accepts_only_a_current_open_import_file()
    {
        var openFiles = new NetsisImportOpenFileDto[]
        {
            new("IMP-2026-001", "320.001", "Supplier", null, null)
        };

        SteelReceiptService.ValidateOpenImportFile("imp-2026-001", openFiles);

        var exception = Assert.Throws<AppException>(() =>
            SteelReceiptService.ValidateOpenImportFile("IMP-2026-CLOSED", openFiles));
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public void Foreign_retry_must_match_the_original_import_file()
    {
        var key = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 18);
        var receipt = new GoodsReceiptHeader
        {
            CorrelationId = key,
            InitiationMode = GoodsReceiptInitiationMode.DirectReceipt,
            ElectronicWaybillNo = "GIB2026AB000001",
            WaybillDate = date,
            TradeType = GoodsReceiptTradeType.Foreign,
            ImportFileNumber = "IMP-2026-001"
        };

        Assert.True(SteelReceiptService.IsCompatibleReplay(
            receipt,key,SteelReceiptConversionMode.Direct,
            null,"GIB2026AB000001",date,
            GoodsReceiptTradeType.Foreign,"IMP-2026-001"));
        Assert.False(SteelReceiptService.IsCompatibleReplay(
            receipt,key,SteelReceiptConversionMode.Direct,
            null,"GIB2026AB000001",date,
            GoodsReceiptTradeType.Foreign,"IMP-2026-002"));
    }
}
