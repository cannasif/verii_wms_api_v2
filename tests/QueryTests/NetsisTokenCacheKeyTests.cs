using verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class NetsisTokenCacheKeyTests
{
    [Fact]
    public void Netsis_login_defaults_to_numeric_mssql_provider_code()
    {
        Assert.Equal("0", new NetsisRestOptions().DbType);
    }

    [Fact]
    public void Token_cache_is_isolated_by_authenticated_branch()
    {
        var options = new NetsisOptions
        {
            Rest = new NetsisRestOptions
            {
                DbName = "TESTDB",
                Username = "netsis-user"
            }
        };

        var branchOne = NetsisTokenService.BuildCacheKey(options, "1");
        var branchTwo = NetsisTokenService.BuildCacheKey(options, "2");

        Assert.NotEqual(branchOne, branchTwo);
        Assert.Contains("branch:1", branchOne);
        Assert.Contains("branch:2", branchTwo);
    }
}
