using System.Text.RegularExpressions;
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
        var endpoints = typeof(AuthController).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods()
                .SelectMany(method => method.GetCustomAttributes(inherit: true)
                    .OfType<HttpMethodAttribute>()
                    .Where(attribute => attribute.Template?.Contains("paged", StringComparison.OrdinalIgnoreCase) == true)
                    .Select(attribute => new { Type = type, Method = method, Attribute = attribute })))
            .ToArray();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
        {
            Assert.Contains("POST", endpoint.Attribute.HttpMethods, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(endpoint.Method.GetParameters(), parameter => parameter.ParameterType == typeof(PagedRequest));
        });
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
}
