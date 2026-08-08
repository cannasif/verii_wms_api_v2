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

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Api_response_marks_utc_date_times_with_an_explicit_utc_designator(DateTimeKind kind)
    {
        var value = DateTime.SpecifyKind(new DateTime(2026, 8, 8, 19, 48, 18), kind);

        var json = JsonSerializer.Serialize(value, WmsJsonSerialization.ResponseOptions);

        Assert.Equal("\"2026-08-08T19:48:18Z\"", json);
    }

    [Fact]
    public void Api_request_treats_legacy_offsetless_date_times_as_utc()
    {
        var value = JsonSerializer.Deserialize<DateTime>(
            "\"2026-08-08T19:48:18\"",
            WmsJsonSerialization.ResponseOptions);

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 8, 8, 19, 48, 18, DateTimeKind.Utc), value);
    }
}
