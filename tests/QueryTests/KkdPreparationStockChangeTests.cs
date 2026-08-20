using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

/// <summary>
/// Toplama sırasında barkodla çözülen grup kaleminde yanlış stok (ör. yanlış beden) okutulmuşsa,
/// hiçbir şey toplanmadığı sürece stok değiştirilebilmelidir. Atamada stoğu belirtilmiş kalemler ile
/// toplaması başlamış kalemler bu kapının dışında kalır.
/// </summary>
public sealed class KkdPreparationStockChangeTests
{
    [Fact]
    public void Group_line_resolved_during_picking_can_be_rebound_while_nothing_is_picked()
    {
        Assert.True(KkdPreparationTaskService.CanChangeStock(
            Line(), KkdRequestStatus.InPreparation, groupOrigin: true, alreadyPicked: false));
    }

    [Fact]
    public void Line_assigned_with_a_stock_is_never_reboundable()
    {
        Assert.False(KkdPreparationTaskService.CanChangeStock(
            Line(), KkdRequestStatus.InPreparation, groupOrigin: false, alreadyPicked: false));
    }

    [Fact]
    public void Picking_of_any_task_sharing_the_request_line_closes_the_door()
    {
        Assert.False(KkdPreparationTaskService.CanChangeStock(
            Line(), KkdRequestStatus.InPreparation, groupOrigin: true, alreadyPicked: true));
    }

    [Fact]
    public void Half_finished_delivery_keeps_the_quantity_allocated_so_the_stock_stays_locked()
    {
        Assert.False(KkdPreparationTaskService.CanChangeStock(
            Line(allocated: 1), KkdRequestStatus.InPreparation, groupOrigin: true, alreadyPicked: false));
    }

    [Fact]
    public void Delivered_line_cannot_change_its_stock()
    {
        Assert.False(KkdPreparationTaskService.CanChangeStock(
            Line(delivered: 1), KkdRequestStatus.InPreparation, groupOrigin: true, alreadyPicked: false));
    }

    [Theory]
    [InlineData(KkdRequestStatus.Completed)]
    [InlineData(KkdRequestStatus.Cancelled)]
    public void Closed_requests_are_immutable(KkdRequestStatus status)
    {
        Assert.False(KkdPreparationTaskService.CanChangeStock(
            Line(), status, groupOrigin: true, alreadyPicked: false));
    }

    /// <summary>
    /// Tezgâhta stok personel karşıdayken seçildiği için grup çözümlemesi hiç oluşmaz; "ayağına olmadı"
    /// düzeltmesi asıl orada gerekir. Sipariş bağı, grup kökeninin yerini tutar.
    /// </summary>
    [Fact]
    public void Order_sourced_line_can_change_its_stock_even_without_a_group_resolution()
    {
        Assert.True(KkdPreparationTaskService.CanChangeStock(
            Line(orderNo: "SIP-001"), KkdRequestStatus.InPreparation, groupOrigin: false, alreadyPicked: false));
    }

    [Fact]
    public void Order_sourced_line_is_locked_once_something_is_picked_onto_the_staging_shelf()
    {
        Assert.False(KkdPreparationTaskService.CanChangeStock(
            Line(orderNo: "SIP-001"), KkdRequestStatus.InPreparation, groupOrigin: false, alreadyPicked: true));
    }

    /// <summary>Stok hiç bağlanmamışsa akış zaten "grubu stoğa bağla"dır; değiştirme kapısı kapalıdır.</summary>
    [Fact]
    public void Unresolved_line_is_not_a_change_but_a_first_binding()
    {
        Assert.False(KkdPreparationTaskService.CanChangeStock(
            new KkdRequestLine { GroupCode = "AYAKKABI" }, KkdRequestStatus.InPreparation,
            groupOrigin: true, alreadyPicked: false));
    }

    private static KkdRequestLine Line(decimal allocated = 0, decimal delivered = 0, string? orderNo = null) => new()
    {
        GroupCode = "AYAKKABI",
        StockId = 42,
        RequestedQuantity = 1,
        AllocatedQuantity = allocated,
        DeliveredQuantity = delivered,
        ExternalOrderNo = orderNo
    };
}
