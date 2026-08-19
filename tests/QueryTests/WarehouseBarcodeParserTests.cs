using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseBarcodeParserTests
{
    [Fact]
    public void Parses_human_readable_gs1_stock_lot_serial_quantity_and_dates()
    {
        var result = WarehouseBarcodeParser.TryParse(
            "(01)08691234567890(10)LOT-2026-A(11)260725(17)270731(21)SER-0001(30)1");

        Assert.NotNull(result);
        Assert.Equal("08691234567890", result.ProductCode);
        Assert.Equal("LOT-2026-A", result.LotNo);
        Assert.Equal("SER-0001", result.SerialNo);
        Assert.Equal(1m, result.Quantity);
        Assert.Equal(new DateOnly(2026, 7, 25), result.ManufacturingDate);
        Assert.Equal(new DateOnly(2027, 7, 31), result.ExpirationDate);
    }

    [Fact]
    public void Parses_scanner_gs1_with_symbology_identifier_and_group_separator()
    {
        var groupSeparator = (char)29;
        var raw = $"]C1010869123456789010LOT-77{groupSeparator}21SER-77{groupSeparator}301";

        var result = WarehouseBarcodeParser.TryParse(raw);

        Assert.NotNull(result);
        Assert.Equal("08691234567890", result.ProductCode);
        Assert.Equal("LOT-77", result.LotNo);
        Assert.Equal("SER-77", result.SerialNo);
        Assert.Equal(1m, result.Quantity);
    }

    [Fact]
    public void Uses_last_day_of_month_when_gs1_expiry_day_is_zero()
    {
        var result = WarehouseBarcodeParser.TryParse("(01)08691234567890(17)270200");

        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2027, 2, 28), result.ExpirationDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("0101")]
    [InlineData("100134-1")]
    [InlineData("10LOT-77")]
    public void Rejects_values_that_are_not_supported_gs1(string value)
    {
        Assert.Null(WarehouseBarcodeParser.TryParse(value));
    }

    [Fact]
    public void Still_parses_human_readable_lot_only_gs1()
    {
        var result = WarehouseBarcodeParser.TryParse("(10)LOT-77");

        Assert.NotNull(result);
        Assert.Equal("LOT-77", result.LotNo);
        Assert.Null(result.ProductCode);
    }
}
