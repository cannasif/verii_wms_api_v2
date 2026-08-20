using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.QueryTests;

/// <summary>
/// Tezgâh akışı: personel karşıdayken açık sipariş kaleminden talep üretilip toplama başlatılır.
/// Beden/stok seçimi bu adımda yapıldığı için stoğu belirsiz kalem oluşmaz.
/// </summary>
public sealed class KkdOrderPickingTests
{
    [Fact]
    public void A_line_without_a_chosen_stock_is_rejected_because_the_size_is_picked_at_the_counter()
    {
        var exception = Assert.Throws<AppException>(() => KkdOrderPickingService.Validate(Request(stockId: 0)));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void The_same_order_line_cannot_be_picked_twice_in_one_go()
    {
        var request = Request() with
        {
            Lines = [Line(), Line()]
        };

        Assert.Throws<AppException>(() => KkdOrderPickingService.Validate(request));
    }

    [Fact]
    public void A_valid_counter_request_passes()
    {
        KkdOrderPickingService.Validate(Request());
    }

    [Fact]
    public void Stock_group_becomes_the_request_line_group()
    {
        Assert.Equal("AYAKKABI", KkdOrderPickingService.GroupCodeOf(
            new StockEntity { BranchCode = "0", ErpStockCode = "AYK-45", GroupCode = "ayakkabi" }));
    }

    /// <summary>
    /// Hak matrisinde karşılığı olmayan sipariş kalemi de toplanabilmelidir; grubu olmayan stok kendi
    /// koduyla gruplanır ve miktar hak dışı sayılarak müdür kararına düşer.
    /// </summary>
    [Fact]
    public void A_stock_outside_the_entitlement_matrix_falls_back_to_its_own_code()
    {
        Assert.Equal("SRV-001", KkdOrderPickingService.GroupCodeOf(
            new StockEntity { BranchCode = "0", ErpStockCode = "srv-001", GroupCode = null }));
    }

    private static KkdOrderPickingStartRequest Request(long stockId = 42) => new(
        Guid.NewGuid(), EmployeeId: 7, WarehouseId: 3, Description: null, [Line(stockId)]);

    private static KkdOrderPickingLineRequest Line(long stockId = 42) =>
        new("SIP-001", OrderLineId: 11, stockId, Quantity: 1);
}
