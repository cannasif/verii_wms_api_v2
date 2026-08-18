using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class StockTrackingPolicySearchQueryTests
{
    [Fact]
    public void Default_page_sort_is_translated_to_policy_id()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest();

        var query = StockTrackingPolicyService.BuildPagedQuery(
            request,
            db.Set<StockTrackingPolicy>(),
            db.Set<Stock>());
        var sql = PagedQueryExtensions.RewriteProjectionMemberAccess(query).ToQueryString();

        Assert.Contains("LEFT JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "LEFT JOIN"));
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(StockTrackingPolicyRow), sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Selected_display_name_uses_shared_turkish_pattern()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "MUTI", SearchFields = ["displayName"] };

        var query = StockTrackingPolicyService.BuildPagedQuery(
            request,
            db.Set<StockTrackingPolicy>(),
            db.Set<Stock>());
        var sql = PagedQueryExtensions.RewriteProjectionMemberAccess(query).ToQueryString();

        Assert.Contains("DisplayName", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[iIİıîÎ]", sql, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(sql, "LEFT JOIN"));
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }
}
