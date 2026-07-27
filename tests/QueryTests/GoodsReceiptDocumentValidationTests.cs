using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptDocumentValidationTests
{
    [Fact]
    public void Import_flow_accepts_missing_waybill_reference()
    {
        GoodsReceiptOperationsService.ValidateDocumentReference(
            null,
            null,
            null,
            GoodsReceiptExecutionMode.Import);
    }

    [Fact]
    public void Manual_flow_requires_one_waybill_reference()
    {
        var exception = Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateDocumentReference(
                null,
                null,
                null,
                GoodsReceiptExecutionMode.Manual));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("zorunludur", exception.Message);
    }

    [Fact]
    public void Two_waybill_reference_types_are_rejected()
    {
        var exception = Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateDocumentReference(
                "123456789012345",
                "ABC2026000000001",
                new DateOnly(2026, 7, 27),
                GoodsReceiptExecutionMode.Import));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("birlikte girilemez", exception.Message);
    }

    [Fact]
    public void Supplied_waybill_reference_requires_date()
    {
        var exception = Assert.Throws<AppException>(() =>
            GoodsReceiptOperationsService.ValidateDocumentReference(
                "123456789012345",
                null,
                null,
                GoodsReceiptExecutionMode.Import));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("tarihi zorunludur", exception.Message);
    }
}
