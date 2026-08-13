using System.Text.Json;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptNetsisOrderLinkTests
{
    [Fact]
    public void ItemSlipLine_UsesNetsisOrderLinkAndPriceWireMembers()
    {
        var line = new NetsisItemSlipLine
        {
            StokKodu = "STK-01",
            Miktar = 5,
            NetFiyat = 12.50m,
            BrutFiyat = 12.50m,
            SiparisNumarasi = "S202600000001",
            SiparisKontrol = 3,
            ProjeKodu = "PRJ-01"
        };

        var json = JsonSerializer.Serialize(line);

        Assert.Contains("\"STra_NF\":12.50", json);
        Assert.Contains("\"STra_BF\":12.50", json);
        Assert.Contains("\"STra_SIPNUM\":\"S202600000001\"", json);
        Assert.Contains("\"STra_SIPKONT\":3", json);
        Assert.Contains("\"ProjeKodu\":\"PRJ-01\"", json);
        Assert.DoesNotContain("\"SiparisNo\"", json);
    }

    [Fact]
    public void ItemSlipHeader_UsesNetsisDeliveryAndPricingDateWireMembers()
    {
        var header = new NetsisItemSlipHeader
        {
            FiyatTarihi = "30.07.2026",
            SiparisTeslimTarihi = "05.08.2026",
            ProjeKodu = "PRJ-01"
        };

        var json = JsonSerializer.Serialize(header);

        Assert.Contains("\"FIYATTARIHI\":\"30.07.2026\"", json);
        Assert.Contains("\"SIPARIS_TEST\":\"05.08.2026\"", json);
        Assert.Contains("\"Proje_Kodu\":\"PRJ-01\"", json);
    }

    [Fact]
    public void GoodsReceiptDeliveryDate_UsesErpPostingLocalDateInsteadOfPurchaseOrderDate()
    {
        var localZone = TimeZoneInfo.CreateCustomTimeZone(
            "WMS test timezone",
            TimeSpan.FromHours(3),
            "WMS test timezone",
            "WMS test timezone");
        var provider = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 13, 21, 15, 30, TimeSpan.Zero),
            localZone);

        var result = ErpPostingService.ResolveGoodsReceiptDeliveryDate(provider);

        Assert.Equal(new DateTime(2026, 8, 14, 0, 15, 30), result);
    }

    [Fact]
    public void HeaderProject_UsesOnlyCommonLinkedOrderProject()
    {
        var sameProject = new[]
        {
            Source("S1", 1, "PRJ-01"),
            Source("S2", 2, "prj-01")
        };
        var mixedProject = new[]
        {
            Source("S1", 1, "PRJ-01"),
            Source("S2", 2, "PRJ-02")
        };

        Assert.Equal("PRJ-01", ErpPostingService.ResolveHeaderProjectCode(sameProject));
        Assert.Equal("0", ErpPostingService.ResolveHeaderProjectCode(mixedProject));
        Assert.Equal("0", ErpPostingService.ResolveHeaderProjectCode([]));
    }

    [Fact]
    public void OrderAllocation_ConsumesSameOrderLinesByAscendingSipKont()
    {
        var queue = new List<ErpPostingService.GoodsReceiptOrderAllocationState>
        {
            State("SIP01", 2, 7),
            State("SIP01", 3, 7),
            State("SIP01", 4, 7)
        };

        var result = ErpPostingService.AllocateOrderQuantity(queue, 10, "STOK1");

        Assert.Collection(
            result,
            x =>
            {
                Assert.Equal(2, x.OrderRow.OrderLineSequence);
                Assert.Equal(7, x.Quantity);
            },
            x =>
            {
                Assert.Equal(3, x.OrderRow.OrderLineSequence);
                Assert.Equal(3, x.Quantity);
            });
        Assert.Equal(0, queue[0].RemainingQuantity);
        Assert.Equal(4, queue[1].RemainingQuantity);
        Assert.Equal(7, queue[2].RemainingQuantity);
    }

    [Fact]
    public void OrderAllocation_ConsumesAllSameOrderLinesForFullReceipt()
    {
        var queue = new List<ErpPostingService.GoodsReceiptOrderAllocationState>
        {
            State("SIP01", 2, 7),
            State("SIP01", 3, 7),
            State("SIP01", 4, 7)
        };

        var result = ErpPostingService.AllocateOrderQuantity(queue, 21, "STOK1");

        Assert.Equal(
            [(2, 7m), (3, 7m), (4, 7m)],
            result.Select(x => (x.OrderRow.OrderLineSequence, x.Quantity)).ToArray());
        Assert.All(queue, state => Assert.Equal(0, state.RemainingQuantity));
    }

    [Fact]
    public void OrderAllocation_CanSplitAcrossDifferentOrdersAndSequences()
    {
        var queue = new List<ErpPostingService.GoodsReceiptOrderAllocationState>
        {
            State("SIP01", 2, 7),
            State("SIP01", 3, 7),
            State("SIP02", 1, 5)
        };

        var result = ErpPostingService.AllocateOrderQuantity(queue, 17, "STOK1");

        Assert.Equal(
            [("SIP01", 2, 7m), ("SIP01", 3, 7m), ("SIP02", 1, 3m)],
            result.Select(x => (x.OrderRow.OrderNumber, x.OrderRow.OrderLineSequence, x.Quantity)).ToArray());
    }

    [Fact]
    public void OrderAllocation_RejectsQuantityAboveSelectedOrderCapacity()
    {
        var queue = new List<ErpPostingService.GoodsReceiptOrderAllocationState>
        {
            State("SIP01", 2, 7)
        };

        var exception = Assert.ThrowsAny<Exception>(
            () => ErpPostingService.AllocateOrderQuantity(queue, 8, "STOK1"));

        Assert.Contains("1 kadarı", exception.Message);
    }

    private static ErpPostingService.GoodsReceiptOrderAllocationState State(
        string orderNumber,
        int orderLineSequence,
        decimal quantity) =>
        new("STOK1\u001F", Source(orderNumber, orderLineSequence, "PRJ-01"), quantity);

    private static GoodsReceiptOrderSourceLine Source(
        string orderNumber,
        int orderLineSequence,
        string? projectCode) => new(
            orderNumber,
            orderLineSequence,
            orderLineSequence,
            "STK-01",
            "Stok",
            "ADET",
            null,
            null,
            "320.001",
            "Tedarikçi",
            0,
            1,
            projectCode,
            new DateTime(2026, 7, 30),
            new DateTime(2026, 8, 5),
            12.50m,
            13.25m,
            10,
            0,
            10,
            0,
            10);

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow,
        TimeZoneInfo localTimeZone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => localTimeZone;
    }
}
