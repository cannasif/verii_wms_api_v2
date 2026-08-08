using System.Text.Json;
using verii_wms_api_v2.Shared.Host.Localization;
using verii_wms_api_v2.Shared.Host.Serialization;

namespace verii_wms_api_v2.Shared.Host.Middleware;

public static class ApiStatusCodeResponseWriter
{
    public static async Task WriteAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (context.Response.HasStarted || context.Response.StatusCode < 400) return;

        var resolver = context.RequestServices.GetRequiredService<WmsApiMessageResolver>();
        var message = resolver.Resolve(context.Response.StatusCode, null, false);
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(
                ApiResponse<object>.Error(message.Text, context.TraceIdentifier, message.Code),
                WmsJsonSerialization.ResponseOptions),
            cancellationToken);
    }
}
