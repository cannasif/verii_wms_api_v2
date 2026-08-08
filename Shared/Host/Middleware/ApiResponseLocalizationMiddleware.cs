using System.Text.Json;
using System.Text.Json.Nodes;
using verii_wms_api_v2.Shared.Host.Localization;
using verii_wms_api_v2.Shared.Host.Serialization;

namespace verii_wms_api_v2.Shared.Host.Middleware;

/// <summary>
/// Applies the API message contract to every JSON envelope, including legacy
/// controllers that still pass a presentation string to ApiResponse.Ok/Error.
/// </summary>
public sealed class ApiResponseLocalizationMiddleware(RequestDelegate next, WmsApiMessageResolver resolver)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (buffer.Length == 0
            || context.Response.StatusCode == StatusCodes.Status204NoContent
            || !IsJson(context.Response.ContentType))
        {
            await CopyAsync(buffer, originalBody);
            return;
        }

        buffer.Position = 0;
        JsonNode? root;
        try
        {
            root = await JsonNode.ParseAsync(buffer);
        }
        catch (JsonException)
        {
            await CopyAsync(buffer, originalBody);
            return;
        }

        if (root is not JsonObject envelope)
        {
            await CopyAsync(buffer, originalBody);
            return;
        }

        var messageProperty = FindProperty(envelope, "message");
        var codeProperty = FindProperty(envelope, "messageCode");
        var success = ReadBoolean(envelope, "success") ?? context.Response.StatusCode < 400;
        var rawMessage = ReadString(envelope, messageProperty);
        var existingCode = ReadString(envelope, codeProperty);

        if (string.IsNullOrWhiteSpace(existingCode))
        {
            var localized = resolver.Resolve(context.Response.StatusCode, rawMessage, success);
            envelope[messageProperty ?? "message"] = localized.Text;
            envelope[codeProperty ?? "messageCode"] = localized.Code;
        }

        context.Response.ContentLength = null;
        await JsonSerializer.SerializeAsync(originalBody, envelope, WmsJsonSerialization.ResponseOptions);
    }

    private static bool IsJson(string? contentType) =>
        contentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true;

    private static string? FindProperty(JsonObject envelope, string name) =>
        envelope.Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    private static bool? ReadBoolean(JsonObject envelope, string name)
    {
        var key = FindProperty(envelope, name);
        return key is null ? null : envelope[key]?.GetValue<bool>();
    }

    private static string? ReadString(JsonObject envelope, string? key) =>
        key is null ? null : envelope[key]?.GetValue<string>();

    private static async Task CopyAsync(Stream source, Stream destination)
    {
        source.Position = 0;
        await source.CopyToAsync(destination);
    }
}
