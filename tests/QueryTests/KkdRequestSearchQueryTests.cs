using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdRequestSearchQueryTests
{
    [Fact]
    public void Header_sort_keeps_request_lines_out_of_item_and_count_queries()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { SortBy = "requestNo", SortDirection = "asc" };

        var itemSql = BuildQuery(db, request, db.Set<KkdRequest>()).ToQueryString();
        var countSql = BuildCountQuery(db, request, db.Set<KkdRequest>()).ToQueryString();

        Assert.DoesNotContain("RII_KKD_REQUEST_LINE", itemSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_KKD_REQUEST_LINE", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_sort_uses_one_grouped_source_only_in_item_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { SortBy = "requestedQuantity", SortDirection = "desc" };

        var itemSql = BuildQuery(db, request, db.Set<KkdRequest>()).ToQueryString();
        var countSql = BuildCountQuery(db, request, db.Set<KkdRequest>()).ToQueryString();

        Assert.Contains("GROUP BY", itemSql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(itemSql, "RII_KKD_REQUEST_LINE"));
        Assert.DoesNotContain("RII_KKD_REQUEST_LINE", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_filter_uses_one_grouped_source_in_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("unresolvedLineCount", "gt", "0")]
        };

        var sql = BuildCountQuery(db, request, db.Set<KkdRequest>()).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "RII_KKD_REQUEST_LINE"));
    }

    [Fact]
    public void Selected_header_search_does_not_add_request_lines()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "KKD",
            SearchFields = ["requestNo"],
            SortBy = "id",
            SortDirection = "desc"
        };
        var searched = KkdRequestService.ApplyPagedSearch(db.Set<KkdRequest>(), request);

        var sql = BuildQuery(db, request, searched).ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_KKD_REQUEST_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Nasif")]
    [InlineData("Nasıf")]
    [InlineData("MUTI")]
    [InlineData("MUTİ")]
    public void Employee_name_search_uses_the_shared_ascii_turkish_pattern(string search)
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = search,
            SearchFields = ["employeeName"]
        };

        var sql = KkdRequestService.ApplyPagedSearch(db.Set<KkdRequest>(), request).ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[iIİıîÎ]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RII_KKD_REQUEST_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selected_line_count_search_uses_only_its_required_subquery()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "2",
            SearchFields = ["totalLineCount"],
            SortBy = "id",
            SortDirection = "desc"
        };
        var searched = KkdRequestService.ApplyPagedSearch(db.Set<KkdRequest>(), request);

        var sql = BuildQuery(db, request, searched).ToQueryString();

        Assert.Equal(1, CountOccurrences(sql, "RII_KKD_REQUEST_LINE"));
    }

    private static IQueryable<KkdRequestGridRow> BuildQuery(
        WmsDbContext db, PagedRequest request, IQueryable<KkdRequest> requests) =>
        KkdRequestService.BuildPagedQuery(request, requests, db.Set<KkdRequestLine>());

    private static IQueryable<long> BuildCountQuery(
        WmsDbContext db, PagedRequest request, IQueryable<KkdRequest> requests) =>
        KkdRequestService.BuildCountQuery(request, requests, db.Set<KkdRequestLine>());

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }

    private static int CountOccurrences(string value, string expected) =>
        value.Split(expected, StringSplitOptions.None).Length - 1;
}
