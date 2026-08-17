using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Shipping.Application;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ShippingSearchQueryTests
{
    [Fact]
    public void Header_sort_keeps_lines_out_of_the_main_grid_query()
    {
        using var db = SqlServerContext();

        var sql = BuildQuery(db, new PagedRequest
        {
            SortBy = "id",
            SortDirection = "desc"
        }).ToQueryString();

        Assert.DoesNotContain("RII_SH_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_sort_uses_one_grouped_line_source_in_the_main_query()
    {
        using var db = SqlServerContext();

        var sql = BuildQuery(db, new PagedRequest
        {
            SortBy = "requestedQuantity",
            SortDirection = "desc"
        }).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "RII_SH_LINE"));
    }

    [Fact]
    public void Field_scoped_header_search_keeps_lines_out_of_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "SH-",
            SearchFields = ["documentNo"]
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("RII_SH_HEADER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_SH_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_sort_keeps_lines_out_of_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            SortBy = "requestedQuantity",
            SortDirection = "desc"
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_SH_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_filter_uses_one_grouped_line_source_in_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("lineCount", "gt", "1")]
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "RII_SH_LINE"));
    }

    private static IQueryable<ShipmentGridRow> BuildQuery(WmsDbContext db, PagedRequest request) =>
        ShippingService.BuildPagedQuery(
            request,
            db.Set<ShipmentHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(),
            db.Set<ShipmentLine>());

    private static IQueryable<long> BuildCountQuery(WmsDbContext db, PagedRequest request) =>
        ShippingService.BuildCountQuery(
            request,
            db.Set<ShipmentHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(),
            db.Set<ShipmentLine>());

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
