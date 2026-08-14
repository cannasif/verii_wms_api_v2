using System.Text.Json;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProductionTransferNetsisPayloadContractTests
{
    [Theory]
    [InlineData("E", "H", "H")]
    [InlineData("H", "E", "H")]
    [InlineData("H", "H", "E")]
    [InlineData("Y", "H", "H")]
    [InlineData("1", "H", "H")]
    public void Netsis_transfer_requires_serial_when_any_transfer_tracking_flag_is_enabled(
        string inputSerial,
        string outputSerial,
        string serialBalance)
    {
        var rule = new NetsisStockTrackingDto(
            0, "STK-001", inputSerial, outputSerial, serialBalance, "H", "H", "H");

        Assert.True(rule.RequiresWarehouseTransferSerial);
    }

    [Fact]
    public void Serial_mismatch_message_identifies_erp_stock_card_as_the_source()
    {
        var message = ErpPostingService.BuildWarehouseTransferSerialMismatchMessage(
            "01/020", 5m, 2m, wmsRequiresSerial: false, erpRequiresSerial: true);

        Assert.Contains("Netsis stok kartı", message);
        Assert.Contains("ERP'ye gidecek miktar 5", message);
        Assert.Contains("seriyle doğrulanan miktar 2", message);
    }

    [Fact]
    public void Warehouse_transfer_payload_uses_netsis_item_slip_contract()
    {
        var request = new NetsisItemSlipRequest
        {
            FaturaTip = 9,
            KayitliNumaraOtomatikGuncellensin = true,
            FatUst = new NetsisItemSlipHeader
            {
                FisNo = "UT2026000000001",
                BelgeNo = "UT2026000000001",
                Tarih = new DateTime(2026, 8, 14),
                FiiliTarih = new DateTime(2026, 8, 14, 17, 44, 8),
                Tip = 9,
                Tipi = NetsisItemSlipInvoiceType.DomesticClosed,
                SubeKodu = 0,
                DepoKodu = 1,
                ProjeKodu = "PRJ-01"
            },
            Kalems =
            [
                new NetsisItemSlipLine
                {
                    StokKodu = "01/020",
                    Miktar = 1m,
                    CikisDepoKodu = 1,
                    GirisDepoKodu = 2,
                    ConfigurationCode = "YAP-01",
                    SeriNo = "SR-0001",
                    SiparisNumarasi = string.Empty,
                    SiparisKontrol = 0,
                    ProjeKodu = "PRJ-01"
                }
            ]
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
        var root = json.RootElement;
        var header = root.GetProperty("FatUst");
        var line = root.GetProperty("Kalems")[0];

        Assert.Equal(9, root.GetProperty("FaturaTip").GetInt32());
        Assert.Equal("UT2026000000001", header.GetProperty("FATIRS_NO").GetString());
        Assert.Equal(9, header.GetProperty("TIP").GetInt32());
        Assert.Equal(1, header.GetProperty("TIPI").GetInt32());
        Assert.Equal(0, header.GetProperty("SUBE_KODU").GetInt32());
        Assert.Equal(1, header.GetProperty("DEPO_KODU").GetInt32());
        Assert.Equal("01/020", line.GetProperty("StokKodu").GetString());
        Assert.Equal(1m, line.GetProperty("STra_GCMIK").GetDecimal());
        Assert.Equal(1, line.GetProperty("Cikis_Depo_Kodu").GetInt32());
        Assert.Equal(2, line.GetProperty("Gir_Depo_Kodu").GetInt32());
        Assert.Equal("YAP-01", line.GetProperty("YapKod").GetString());
        Assert.Equal("SR-0001", line.GetProperty("SeriNo").GetString());
        Assert.Equal("PRJ-01", line.GetProperty("ProjeKodu").GetString());
    }

    [Fact]
    public void Production_reference_is_kept_in_erp_header_description_not_order_link_fields()
    {
        var description = ErpPostingService.BuildProductionTransferErpDescription(
            "Hat besleme",
            new ErpPostingService.ProductionErpReference(
                "PLN-10", "ISEMRI-42", "OP-20", ProductionTransferPurpose.MaterialSupply));

        Assert.Contains("Hat besleme", description);
        Assert.Contains("İş emri: ISEMRI-42", description);
        Assert.Contains("Plan: PLN-10", description);
        Assert.Contains("Operasyon: OP-20", description);
        Assert.Contains("Amaç: MaterialSupply", description);
    }
}
