using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Routing;

namespace verii_wms_api_v2.Shared.Host.Routing;

public sealed class IisSafeHttpMethodConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        foreach (var selector in action.Selectors.ToArray())
        {
            var methods = selector.ActionConstraints.OfType<HttpMethodActionConstraint>().SelectMany(x => x.HttpMethods).ToArray();
            if (methods.Contains("PUT", StringComparer.OrdinalIgnoreCase)) AddAlias(action, selector, selector.AttributeRouteModel?.Template);
            if (methods.Contains("DELETE", StringComparer.OrdinalIgnoreCase))
            {
                var template = selector.AttributeRouteModel?.Template?.Trim('/'); AddAlias(action, selector, string.IsNullOrWhiteSpace(template) ? "delete" : $"{template}/delete");
            }
        }
    }
    private static void AddAlias(ActionModel action, SelectorModel source, string? template)
    {
        if (action.Selectors.Any(x => string.Equals(x.AttributeRouteModel?.Template ?? "", template ?? "", StringComparison.OrdinalIgnoreCase) && x.ActionConstraints.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).Contains("POST", StringComparer.OrdinalIgnoreCase))) return;
        var alias = new SelectorModel { AttributeRouteModel = source.AttributeRouteModel is null ? null : new AttributeRouteModel(source.AttributeRouteModel) { Template = template, Name = null } };
        alias.ActionConstraints.Add(new HttpMethodActionConstraint(new[] { "POST" })); alias.EndpointMetadata.Add(new HttpMethodMetadata(new[] { "POST" })); action.Selectors.Add(alias);
    }
}
