using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class IncomingInvoiceQuantityConversionTests
{
    [Theory]
    [InlineData(2, 12, 24)]
    [InlineData(1.5, 0.5, 0.75)]
    [InlineData(100, 1, 100)]
    public void Supplier_quantity_is_converted_to_system_base_unit(
        decimal quantity, decimal factor, decimal expected)
    {
        Assert.Equal(
            expected,
            IncomingInvoiceService.ConvertToSystemQuantity(quantity, factor));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 2)]
    public void Invalid_quantity_or_factor_is_rejected(decimal quantity, decimal factor)
    {
        Assert.Throws<AppException>(
            () => IncomingInvoiceService.ConvertToSystemQuantity(quantity, factor));
    }

    [Fact]
    public void Ocr_upload_rejects_spoofed_content_type()
    {
        Assert.Throws<AppException>(() =>
            IncomingInvoiceService.ValidateOcrFileSignature(
                "not a pdf"u8.ToArray(), "application/pdf"));
        IncomingInvoiceService.ValidateOcrFileSignature(
            "%PDF-1.7"u8.ToArray(), "application/pdf");
    }
}
