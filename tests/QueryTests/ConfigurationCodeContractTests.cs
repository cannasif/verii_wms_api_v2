using System.Text.Json;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ConfigurationCodeContractTests
{
    [Fact]
    public void Netsis_item_slip_keeps_legacy_YapKod_wire_field()
    {
        var line = new NetsisItemSlipLine
        {
            StokKodu = "STK-001",
            Miktar = 1,
            ConfigurationCode = "RENK-KIRMIZI"
        };

        var json = JsonSerializer.Serialize(line);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("RENK-KIRMIZI", document.RootElement.GetProperty("YapKod").GetString());
        Assert.False(document.RootElement.TryGetProperty("ConfigurationCode", out _));
    }
}
