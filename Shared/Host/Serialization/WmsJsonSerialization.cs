using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace verii_wms_api_v2.Shared.Host.Serialization;

public static class WmsJsonSerialization
{
    public static JsonSerializerOptions ResponseOptions { get; } = CreateResponseOptions();

    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Keep JSON safe while allowing human-readable Unicode characters such as
        // Turkish ğ, ı, İ, ş, ç, ö and ü to be emitted directly in API responses.
        options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        options.Converters.Add(new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions CreateResponseOptions()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        return options;
    }
}
