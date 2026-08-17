using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptSearchQueryTests
{
    [Fact]
    public void Field_scoped_search_keeps_active_header_scope_without_actor_or_line_joins()
    {
        using var db = SqlServerContext();

        var sql = BuildQuery(db, new PagedRequest
        {
            Search = "TEST",
            SearchFields = ["waybillNo"]
        }).ToQueryString();

        Assert.Contains("RII_GR_HEADER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsDeleted", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_USER_DETAILS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ActualArrivalAtUtc", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AutoPickWithoutConfirmMaxQuantity", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Actor_search_adds_actor_joins_only_when_requested()
    {
        using var db = SqlServerContext();

        var sql = BuildQuery(db, new PagedRequest
        {
            Search = "System Administrator",
            SearchFields = ["createdBy"]
        }).ToQueryString();

        Assert.Contains("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RII_USER_DETAILS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_sort_uses_one_grouped_line_source()
    {
        using var db = SqlServerContext();

        var sql = BuildQuery(db, new PagedRequest
        {
            SortBy = "lineCount",
            SortDirection = "desc"
        }).ToQueryString();

        Assert.Contains("RII_GR_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "RII_GR_LINE"));
        Assert.DoesNotContain("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_sort_does_not_add_lines_to_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            SortBy = "lineCount",
            SortDirection = "desc"
        };

        var sql = GoodsReceiptOperationsService.BuildCountQuery(
            request,
            db.Set<GoodsReceiptHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(),
            db.Set<GoodsReceiptLine>(),
            db.Set<User>(),
            db.Set<UserDetail>()).ToQueryString();

        Assert.Contains("RII_GR_HEADER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_filter_keeps_one_grouped_line_source_in_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("lineCount", "gt", "1")]
        };

        var sql = GoodsReceiptOperationsService.BuildCountQuery(
            request,
            db.Set<GoodsReceiptHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(),
            db.Set<GoodsReceiptLine>(),
            db.Set<User>(),
            db.Set<UserDetail>()).ToQueryString();

        Assert.Contains("RII_GR_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "RII_GR_LINE"));
        Assert.DoesNotContain("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<GoodsReceiptGridRow> BuildQuery(WmsDbContext db, PagedRequest request) =>
        GoodsReceiptOperationsService.BuildPagedQuery(
            request,
            db.Set<GoodsReceiptHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(),
            db.Set<GoodsReceiptLine>(),
            db.Set<User>(),
            db.Set<UserDetail>());

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
