using System.Globalization;
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
        if (!options.Converters.Any(converter => converter is UtcDateTimeJsonConverter))
            options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions CreateResponseOptions()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        return options;
    }

    private sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("A JSON string was expected for a date-time value.");

            var rawValue = reader.GetString();
            if (!DateTime.TryParse(
                    rawValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                    out var value))
                throw new JsonException("The JSON value is not a valid ISO-8601 date-time value.");

            return NormalizeUtc(value);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(NormalizeUtc(value));

        private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
