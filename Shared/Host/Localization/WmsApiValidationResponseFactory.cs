using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Shared.Host.Middleware;

namespace verii_wms_api_v2.Shared.Host.Localization;

public static class WmsApiValidationResponseFactory
{
    public static IActionResult Create(ActionContext context)
    {
        var resolver = context.HttpContext.RequestServices.GetRequiredService<WmsApiMessageResolver>();
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(error =>
                {
                    var raw = string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? error.Exception?.Message
                        : error.ErrorMessage;
                    var code = WmsApiMessageResolver.Classify(StatusCodes.Status400BadRequest, raw, false);
                    return resolver.ResolveCode(code, raw).Text;
                }).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var message = resolver.ResolveCode("ValidationFailed");
        return new BadRequestObjectResult(new ApiResponse<IReadOnlyDictionary<string, string[]>>(
            false,
            errors,
            message.Text,
            context.HttpContext.TraceIdentifier,
            message.Code));
    }
}

