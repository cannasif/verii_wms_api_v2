using System.Text.Json;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
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
            FaturaTip = 5,
            KayitliNumaraOtomatikGuncellensin = true,
            FatUst = new NetsisItemSlipHeader
            {
                FisNo = "UT2026000000001",
                BelgeNo = "UT2026000000001",
                Tarih = new DateTime(2026, 8, 14),
                FiiliTarih = new DateTime(2026, 8, 14, 17, 44, 8),
                Tip = 5,
                Tipi = NetsisItemSlipInvoiceType.Empty,
                CariKod = string.Empty,
                SecondaryCustomerCode = string.Empty,
                WarehouseMovementType = NetsisWarehouseMovementType.Warehouses,
                SourceBranchCode = 0,
                TargetBranchCode = 0,
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
                    DepoKodu = 1,
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

        Assert.Equal(5, root.GetProperty("FaturaTip").GetInt32());
        Assert.Equal("UT2026000000001", header.GetProperty("FATIRS_NO").GetString());
        Assert.Equal(5, header.GetProperty("TIP").GetInt32());
        Assert.Equal(0, header.GetProperty("TIPI").GetInt32());
        Assert.Equal(1, header.GetProperty("AMBHARTUR").GetInt32());
        Assert.Equal(0, header.GetProperty("GCKOD_CIKIS").GetInt32());
        Assert.Equal(0, header.GetProperty("GCKOD_GIRIS").GetInt32());
        Assert.Equal(string.Empty, header.GetProperty("CariKod").GetString());
        Assert.Equal(string.Empty, header.GetProperty("CARI_KOD2").GetString());
        Assert.Equal(0, header.GetProperty("SUBE_KODU").GetInt32());
        Assert.Equal(1, header.GetProperty("DEPO_KODU").GetInt32());
        Assert.Equal("01/020", line.GetProperty("StokKodu").GetString());
        Assert.Equal(1m, line.GetProperty("STra_GCMIK").GetDecimal());
        Assert.Equal(1, line.GetProperty("DEPO_KODU").GetInt32());
        Assert.Equal(2, line.GetProperty("Gir_Depo_Kodu").GetInt32());
        Assert.False(line.TryGetProperty("CikisDepoKodu", out _));
        Assert.False(line.TryGetProperty("GirisDepoKodu", out _));
        Assert.False(line.TryGetProperty("Cikis_Depo_Kodu", out _));
        Assert.Equal("YAP-01", line.GetProperty("YapKod").GetString());
        Assert.Equal("SR-0001", line.GetProperty("SeriNo").GetString());
        Assert.Equal("PRJ-01", line.GetProperty("ProjeKodu").GetString());
    }

    [Fact]
    public void Netsis_destination_warehouse_wire_member_round_trips_as_Gir_Depo_Kodu()
    {
        var source = new NetsisItemSlipLine
        {
            StokKodu = "01/025",
            Miktar = 4,
            DepoKodu = 2,
            CikisDepoKodu = 2,
            GirisDepoKodu = 1
        };

        var json = JsonSerializer.Serialize(source);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("DEPO_KODU").GetInt32());
        Assert.Equal(1, root.GetProperty("Gir_Depo_Kodu").GetInt32());
        Assert.False(root.TryGetProperty("CikisDepoKodu", out _));
        Assert.False(root.TryGetProperty("GirisDepoKodu", out _));

        var roundTrip = JsonSerializer.Deserialize<NetsisItemSlipLine>(json);
        Assert.NotNull(roundTrip);
        Assert.Equal(2, roundTrip!.DepoKodu);
        Assert.Equal(1, roundTrip.GirisDepoKodu);
        Assert.Null(roundTrip.CikisDepoKodu);
    }

    [Fact]
    public void Warehouse_transfer_payload_requires_source_and_target_codes_on_every_line()
    {
        var request = new NetsisItemSlipRequest
        {
            FatUst = new NetsisItemSlipHeader { DepoKodu = 2 },
            Kalems =
            [
                new NetsisItemSlipLine
                {
                    StokKodu = "STK-001",
                    Miktar = 1,
                    DepoKodu = 2,
                    CikisDepoKodu = 2,
                    GirisDepoKodu = 1
                },
                new NetsisItemSlipLine
                {
                    StokKodu = "STK-002",
                    Miktar = 2,
                    DepoKodu = 2,
                    CikisDepoKodu = 2,
                    GirisDepoKodu = 1
                }
            ]
        };

        request.FaturaTip = 5;
        request.FatUst.Tip = 5;
        request.FatUst.Tipi = NetsisItemSlipInvoiceType.Empty;
        request.FatUst.CariKod = string.Empty;
        request.FatUst.SecondaryCustomerCode = string.Empty;
        request.FatUst.WarehouseMovementType = NetsisWarehouseMovementType.Warehouses;
        request.FatUst.SourceBranchCode = 0;
        request.FatUst.TargetBranchCode = 0;

        ErpPostingService.ValidateWarehouseTransferWarehouseCodes(request, 2, 1, 0, 0);

        request.Kalems[1].DepoKodu = null;
        var exception = Assert.Throws<AppException>(() =>
            ErpPostingService.ValidateWarehouseTransferWarehouseCodes(request, 2, 1, 0, 0));
        Assert.Contains("Çıkış depo: 2, giriş depo: 1", exception.Message);
    }

    [Fact]
    public void Same_branch_transfer_always_uses_local_warehouse_transfer_type()
    {
        Assert.Equal(
            NetsisItemSlipDocumentTypes.LocalWarehouseTransfer,
            ErpPostingService.ResolveWarehouseTransferDocumentType(0, 0));
    }

    [Fact]
    public void Cross_branch_transfer_is_blocked_without_required_customer_mapping()
    {
        var exception = Assert.Throws<AppException>(() =>
            ErpPostingService.ResolveWarehouseTransferDocumentType(0, 1));

        Assert.Contains("şube cari kodu eşlemesi", exception.Message);
    }

    [Fact]
    public void Identical_untracked_transfer_lines_are_consolidated_for_erp()
    {
        var lines = new[]
        {
            TransferLine(1m),
            TransferLine(3m)
        };

        var result = ErpPostingService.ConsolidateWarehouseTransferLines(lines);

        var line = Assert.Single(result);
        Assert.Equal(4m, line.Miktar);
        Assert.Null(line.ProjeKodu);
        Assert.Equal(2, line.DepoKodu);
        Assert.Equal(2, line.CikisDepoKodu);
        Assert.Equal(1, line.GirisDepoKodu);
    }

    [Fact]
    public void Serial_or_order_linked_transfer_lines_are_not_consolidated()
    {
        var serialLine = TransferLine(1m);
        serialLine.SeriNo = "SER-1";
        var orderLine = TransferLine(3m);
        orderLine.SiparisNumarasi = "SIP-1";
        orderLine.SiparisKontrol = 1;

        var result = ErpPostingService.ConsolidateWarehouseTransferLines([serialLine, orderLine]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Global_serial_export_policy_removes_serials_from_every_erp_line_when_disabled()
    {
        var request = new NetsisItemSlipRequest
        {
            Kalems =
            [
                new NetsisItemSlipLine { StokKodu = "STK-001", Miktar = 1, SeriNo = "SR-001" },
                new NetsisItemSlipLine { StokKodu = "STK-002", Miktar = 2, SeriNo = "SR-002" }
            ]
        };

        ErpPostingService.ApplySerialExportPolicy(request, sendSerialsToErp: false);

        Assert.All(request.Kalems, line => Assert.Null(line.SeriNo));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request));
        Assert.All(
            json.RootElement.GetProperty("Kalems").EnumerateArray(),
            line => Assert.False(line.TryGetProperty("SeriNo", out _)));
    }

    private static NetsisItemSlipLine TransferLine(decimal quantity) => new()
    {
        StokKodu = "01/025",
        Miktar = quantity,
        DepoKodu = 2,
        CikisDepoKodu = 2,
        GirisDepoKodu = 1,
        Aciklama = "Kalan miktar",
        ProjeKodu = null
    };

    [Fact]
    public void Global_serial_export_policy_preserves_serials_when_enabled()
    {
        var request = new NetsisItemSlipRequest
        {
            Kalems =
            [
                new NetsisItemSlipLine { StokKodu = "STK-001", Miktar = 1, SeriNo = "SR-001" }
            ]
        };

        ErpPostingService.ApplySerialExportPolicy(request, sendSerialsToErp: true);

        Assert.Equal("SR-001", Assert.Single(request.Kalems).SeriNo);
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
