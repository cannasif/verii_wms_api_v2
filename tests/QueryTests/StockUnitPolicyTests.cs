using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class StockUnitPolicyTests
{
    [Fact]
    public void Uses_normalized_stock_card_unit_when_client_omits_unit()
    {
        var stock = Stock(" kg ");

        var result = StockUnitPolicy.Resolve(stock);

        Assert.Equal("KG", result);
    }

    [Fact]
    public void Accepts_matching_client_unit_case_insensitively()
    {
        var stock = Stock("ADET");

        var result = StockUnitPolicy.Resolve(stock, " adet ");

        Assert.Equal("ADET", result);
    }

    [Fact]
    public void Rejects_unit_that_conflicts_with_stock_card()
    {
        var stock = Stock("KG");

        Assert.Throws<AppException>(() => StockUnitPolicy.Resolve(stock, "ADET"));
    }

    [Fact]
    public void Rejects_stock_without_a_defined_unit()
    {
        var stock = Stock(string.Empty);

        Assert.Throws<AppException>(() => StockUnitPolicy.Resolve(stock));
    }

    private static StockEntity Stock(string unitCode) => new()
    {
        Id = 42,
        ErpStockCode = "TEST-STOCK",
        StockName = "Test Stock",
        BaseUnitCode = unitCode
    };
}
