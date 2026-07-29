using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Shared.Host.Filters;

/// <summary>
/// Makes the branch signed into the access token authoritative for every authenticated
/// controller operation. Client supplied header/body/query branch values are compatibility
/// inputs only and cannot switch an active session to another branch.
/// </summary>
public sealed class AuthenticatedBranchScopeFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            await next();
            return;
        }

        var branchCode = context.HttpContext.User.FindFirst(JwtTokenIssuer.BranchCodeClaim)?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(branchCode))
            throw AppException.Unauthorized("Oturum şube bilgisi geçersiz. Lütfen yeniden giriş yapın.");

        context.HttpContext.Items["BranchCode"] = branchCode;
        context.HttpContext.Request.Headers["X-Branch-Code"] = branchCode;

        foreach (var argument in context.ActionArguments.ToArray())
        {
            if (argument.Key.Equals("branchCode", StringComparison.OrdinalIgnoreCase)
                && argument.Value is string or null)
            {
                context.ActionArguments[argument.Key] = branchCode;
                continue;
            }

            BranchScopeObjectGraph.Apply(argument.Value, branchCode);
        }

        await next();
    }
}

internal static class BranchScopeObjectGraph
{
    public static void Apply(object? value, string branchCode) =>
        Apply(value, branchCode, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);

    private static void Apply(
        object? value,
        string branchCode,
        HashSet<object> visited,
        int depth)
    {
        if (value is null || depth > 8 || IsTerminal(value.GetType()))
            return;
        if (!visited.Add(value))
            return;

        if (value is IDictionary dictionary)
        {
            var nestedValues = new List<object?>();
            var branchKeys = new List<object>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key && key.Equals("branchCode", StringComparison.OrdinalIgnoreCase))
                    branchKeys.Add(entry.Key);
                else
                    nestedValues.Add(entry.Value);
            }
            foreach (var branchKey in branchKeys)
                dictionary[branchKey] = branchCode;
            foreach (var nestedValue in nestedValues)
                Apply(nestedValue, branchCode, visited, depth + 1);
            return;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
                Apply(item, branchCode, visited, depth + 1);
            return;
        }

        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;

            if (property.Name.Equals("BranchCode", StringComparison.OrdinalIgnoreCase)
                && property.PropertyType == typeof(string)
                && property.SetMethod is not null)
            {
                property.SetValue(value, branchCode);
                continue;
            }

            if (property.GetMethod is not null)
                Apply(property.GetValue(value), branchCode, visited, depth + 1);
        }
    }

    private static bool IsTerminal(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(DateOnly)
        || type == typeof(TimeOnly)
        || type == typeof(Guid);
}
