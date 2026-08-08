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
    private static readonly HashSet<string> NestedMessageProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "validationMessage", "matchMessage", "warningMessage", "errorMessage",
        "lastErrorMessage", "lastError", "statusMessage", "resultMessage"
    };

    private static readonly HashSet<string> MessageContextProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "rowNumber", "status", "reasonCode", "code", "severity", "isConfigured",
        "isAllowed", "isSuccessful", "success"
    };

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
        else
        {
            var localized = resolver.ResolveCode(existingCode, rawMessage);
            envelope[messageProperty ?? "message"] = localized.Text;
        }

        foreach (var property in envelope.ToList())
        {
            if (string.Equals(property.Key, messageProperty, StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Key, codeProperty, StringComparison.OrdinalIgnoreCase))
                continue;

            LocalizeNestedMessages(property.Value);
        }

        context.Response.ContentLength = null;
        await JsonSerializer.SerializeAsync(originalBody, envelope, WmsJsonSerialization.ResponseOptions);
    }

    private void LocalizeNestedMessages(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array) LocalizeNestedMessages(item);
            return;
        }

        if (node is not JsonObject value) return;

        foreach (var property in value.ToList())
        {
            if (property.Value is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var rawMessage)
                && !string.IsNullOrWhiteSpace(rawMessage)
                && ShouldLocalize(value, property.Key))
            {
                var success = ReadNestedSuccess(value);
                var localized = resolver.Resolve(
                    success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest,
                    rawMessage,
                    success);
                value[property.Key] = localized.Text;
            }
            else
            {
                LocalizeNestedMessages(property.Value);
            }
        }
    }

    private static bool ShouldLocalize(JsonObject owner, string propertyName) =>
        NestedMessageProperties.Contains(propertyName)
        || (string.Equals(propertyName, "message", StringComparison.OrdinalIgnoreCase)
            && owner.Any(property => MessageContextProperties.Contains(property.Key)));

    private static bool ReadNestedSuccess(JsonObject owner)
    {
        foreach (var propertyName in new[] { "success", "isSuccessful", "isAllowed" })
        {
            var property = FindProperty(owner, propertyName);
            if (property is not null && owner[property] is JsonValue value && value.TryGetValue<bool>(out var result))
                return result;
        }

        var statusProperty = FindProperty(owner, "status");
        var status = ReadString(owner, statusProperty);
        return status is not null && (status.Contains("success", StringComparison.OrdinalIgnoreCase)
            || status.Contains("complete", StringComparison.OrdinalIgnoreCase)
            || status.Contains("valid", StringComparison.OrdinalIgnoreCase)
            || status.Contains("created", StringComparison.OrdinalIgnoreCase));
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
