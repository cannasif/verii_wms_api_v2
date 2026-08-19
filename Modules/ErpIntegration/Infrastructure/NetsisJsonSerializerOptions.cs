using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;

internal static class NetsisJsonSerializerOptions
{
    internal static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
