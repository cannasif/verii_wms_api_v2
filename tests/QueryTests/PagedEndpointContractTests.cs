using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using verii_wms_api_v2.Modules.Identity.Api;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class PagedEndpointContractTests
{
    [Fact]
    public void Every_runtime_paged_action_is_post_and_accepts_the_shared_request_contract()
    {
        var endpoints = RuntimePagedEndpoints();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
        {
            Assert.Contains("POST", endpoint.Attribute.HttpMethods, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(endpoint.Method.GetParameters(), parameter => parameter.ParameterType == typeof(PagedRequest));
        });
    }

    [Fact]
    public void Runtime_inventory_expands_to_every_live_paged_route()
    {
        var routes = RuntimePagedEndpoints()
            .SelectMany(endpoint =>
            {
                var controllerRoute = endpoint.Type
                    .GetCustomAttributes(inherit: true)
                    .OfType<RouteAttribute>()
                    .Single()
                    .Template;
                return ExpandRoute($"/{controllerRoute.TrimEnd('/')}/{endpoint.Attribute.Template!.TrimStart('/')}");
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // documentType dört, direction iki gerçek rota üretir. Bu sayı canlı
        // matriste test edilen güncel API yüzeyini bilinçli olarak sabitler.
        Assert.Equal(76, routes.Length);
        Assert.DoesNotContain(routes, route => route.Contains('{'));
    }

    [Fact]
    public void Every_paged_controller_route_uses_post_and_never_get()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var modulesRoot = Path.Combine(repositoryRoot, "Modules");
        Assert.True(Directory.Exists(modulesRoot), $"Modules klasörü bulunamadı: {modulesRoot}");

        var pagedAttributes = Directory
            .EnumerateFiles(modulesRoot, "*Controller.cs", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(
                    File.ReadAllText(file),
                    @"\[(?<attributes>[^\]]*(?:paged|Paged)[^\]]*)\]",
                    RegexOptions.CultureInvariant)
                .Select(match => new
                {
                    File = Path.GetRelativePath(repositoryRoot, file),
                    Attributes = match.Groups["attributes"].Value
                }))
            .Where(item => item.Attributes.Contains("Http", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(pagedAttributes);
        Assert.DoesNotContain(pagedAttributes, item =>
            item.Attributes.Contains("HttpGet", StringComparison.Ordinal));
        Assert.DoesNotContain(pagedAttributes, item =>
            !item.Attributes.Contains("HttpPost", StringComparison.Ordinal));
    }

    private static (Type Type, System.Reflection.MethodInfo Method, HttpMethodAttribute Attribute)[] RuntimePagedEndpoints() =>
        typeof(AuthController).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods()
                .SelectMany(method => method.GetCustomAttributes(inherit: true)
                    .OfType<HttpMethodAttribute>()
                    .Where(attribute => attribute.Template?.Contains("paged", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(attribute => (Type: type, Method: method, Attribute: attribute))))
            .ToArray();

    private static IEnumerable<string> ExpandRoute(string route)
    {
        if (route.Contains("{documentType}", StringComparison.Ordinal))
            return new[] { "request", "rfq", "quote", "order" }
                .Select(value => route.Replace("{documentType}", value, StringComparison.Ordinal));
        if (route.Contains("{direction}", StringComparison.Ordinal))
            return new[] { "IssueToSupplier", "ReceiptFromSupplier" }
                .Select(value => route.Replace("{direction}", value, StringComparison.Ordinal));
        if (route.Contains("{id:long}", StringComparison.Ordinal))
            return [route.Replace("{id:long}", "1", StringComparison.Ordinal)];
        return [route];
    }
}
