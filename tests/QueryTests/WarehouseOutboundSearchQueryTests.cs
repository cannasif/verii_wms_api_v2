using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseOutboundSearchQueryTests
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

        Assert.DoesNotContain("RII_WO_LINE", sql, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(1, CountOccurrences(sql, "RII_WO_LINE"));
    }

    [Fact]
    public void Field_scoped_header_search_keeps_lines_out_of_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "WO-",
            SearchFields = ["documentNo"]
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("RII_WO_HEADER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_WO_LINE", sql, StringComparison.OrdinalIgnoreCase);
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

        Assert.DoesNotContain("RII_WO_LINE", sql, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(1, CountOccurrences(sql, "RII_WO_LINE"));
    }

    private static IQueryable<WarehouseOutboundGridRow> BuildQuery(
        WmsDbContext db,
        PagedRequest request) =>
        WarehouseOutboundService.BuildPagedQuery(
            request,
            db.Set<WarehouseOutboundHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(),
            db.Set<WarehouseOutboundLine>());

    private static IQueryable<long> BuildCountQuery(
        WmsDbContext db,
        PagedRequest request) =>
        WarehouseOutboundService.BuildCountQuery(
            request,
            db.Set<WarehouseOutboundHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(),
            db.Set<WarehouseOutboundLine>());

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
