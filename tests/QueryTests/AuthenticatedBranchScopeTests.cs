using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared.Host.Filters;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class AuthenticatedBranchScopeTests
{
    [Fact]
    public void Jwt_contains_the_branch_bound_to_the_login_session()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "integration-test-secret-key-with-at-least-32-bytes",
                ["JwtSettings:Issuer"] = "wms-tests",
                ["JwtSettings:Audience"] = "wms-tests",
                ["JwtSettings:AccessTokenMinutes"] = "15"
            })
            .Build();
        var issuer = new JwtTokenIssuer(configuration);
        var user = new User
        {
            Id = 42,
            Username = "operator",
            Email = "operator@example.test",
            Role = "user",
            Detail = new UserDetail { FirstName = "Test", LastName = "Operator" }
        };

        var result = issuer.CreateAccessToken(user, "12");
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Value);

        Assert.Equal("12", token.Claims.Single(x => x.Type == JwtTokenIssuer.BranchCodeClaim).Value);
        Assert.Equal("42", token.Claims.Single(x => x.Type == ClaimTypes.NameIdentifier).Value);
    }

    [Fact]
    public void Request_body_branch_is_overwritten_by_the_authenticated_branch()
    {
        var request = new ResolveGoodsReceiptQualityRequest("999", [10, 20]);

        BranchScopeObjectGraph.Apply(request, "12");

        Assert.Equal("12", request.BranchCode);
    }

    [Fact]
    public void Nested_branch_values_cannot_switch_the_authenticated_branch()
    {
        var request = new NestedRequest
        {
            BranchCode = "999",
            Child = new NestedRequest { BranchCode = "888" }
        };

        BranchScopeObjectGraph.Apply(request, "12");

        Assert.Equal("12", request.BranchCode);
        Assert.Equal("12", request.Child!.BranchCode);
    }

    private sealed class NestedRequest
    {
        public string BranchCode { get; set; } = string.Empty;
        public NestedRequest? Child { get; init; }
    }
}
