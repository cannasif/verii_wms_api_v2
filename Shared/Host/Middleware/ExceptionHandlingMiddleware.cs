using System.Text.Json;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Host.Serialization;

namespace verii_wms_api_v2.Shared.Host.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (AppException exception) { await WriteError(context, exception.StatusCode, exception.Message); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request error. TraceId: {TraceId}", context.TraceIdentifier);
            await WriteError(context, StatusCodes.Status500InternalServerError, "Beklenmeyen bir sunucu hatası oluştu.");
        }
    }

    private static async Task WriteError(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(
                ApiResponse<object>.Error(message, context.TraceIdentifier),
                WmsJsonSerialization.ResponseOptions));
    }
}
