using System.Text.Json;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Host.Localization;
using verii_wms_api_v2.Shared.Host.Serialization;

namespace verii_wms_api_v2.Shared.Host.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    WmsApiMessageResolver messageResolver)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (AppException exception)
        {
            var localized = messageResolver.Resolve(exception.StatusCode, exception.Message, false);
            await WriteError(context, exception.StatusCode, localized);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request error. TraceId: {TraceId}", context.TraceIdentifier);
            await WriteError(
                context,
                StatusCodes.Status500InternalServerError,
                messageResolver.Resolve(StatusCodes.Status500InternalServerError, null, false));
        }
    }

    private static async Task WriteError(HttpContext context, int statusCode, WmsApiLocalizedMessage message)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(
                ApiResponse<object>.Error(message.Text, context.TraceIdentifier, message.Code),
                WmsJsonSerialization.ResponseOptions));
    }
}
