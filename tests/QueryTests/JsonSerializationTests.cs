using System.Text.Json;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Host.Serialization;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class JsonSerializationTests
{
    [Fact]
    public void Api_response_keeps_Turkish_characters_human_readable()
    {
        const string message = "Seçilen ürün kalite onayını bekliyor.";
        var response = ApiResponse<object>.Error(message, "trace-1");

        var json = JsonSerializer.Serialize(response, WmsJsonSerialization.ResponseOptions);

        Assert.Contains(message, json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00E7", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u0131", json, StringComparison.OrdinalIgnoreCase);
    }
}
