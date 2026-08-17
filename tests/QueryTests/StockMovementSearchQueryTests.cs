using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class StockMovementSearchQueryTests
{
    [Fact]
    public void Header_search_keeps_entries_and_reversal_join_out_of_paged_queries()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "MANUAL", SearchFields = ["referenceNo"] };

        var itemSql = BuildQuery(db, request).ToQueryString();
        var countSql = BuildCountQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_STOCK_MOVEMENT]", itemSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_STOCK_MOVEMENT]", countSql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(itemSql, "RII_STOCK_MOVEMENT_OPERATION"));
        Assert.Equal(1, CountOccurrences(countSql, "RII_STOCK_MOVEMENT_OPERATION"));
    }

    [Fact]
    public void Entry_summary_sort_uses_one_grouped_source_only_in_item_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { SortBy = "inboundQuantity", SortDirection = "desc" };

        var itemSql = BuildQuery(db, request).ToQueryString();
        var countSql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("GROUP BY", itemSql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(itemSql, "RII_STOCK_MOVEMENT]"));
        Assert.DoesNotContain("RII_STOCK_MOVEMENT]", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Entry_summary_search_uses_one_grouped_source_in_both_queries()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "2", SearchFields = ["entryCount"] };

        var itemSql = BuildQuery(db, request).ToQueryString();
        var countSql = BuildCountQuery(db, request).ToQueryString();

        Assert.Equal(1, CountOccurrences(itemSql, "RII_STOCK_MOVEMENT]"));
        Assert.Equal(1, CountOccurrences(countSql, "RII_STOCK_MOVEMENT]"));
    }

    [Fact]
    public void Status_sort_adds_reversal_join_only_to_item_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { SortBy = "status", SortDirection = "asc" };

        var itemSql = BuildQuery(db, request).ToQueryString();
        var countSql = BuildCountQuery(db, request).ToQueryString();

        Assert.Equal(2, CountOccurrences(itemSql, "RII_STOCK_MOVEMENT_OPERATION"));
        Assert.Equal(1, CountOccurrences(countSql, "RII_STOCK_MOVEMENT_OPERATION"));
        Assert.DoesNotContain("RII_STOCK_MOVEMENT]", itemSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Status_filter_adds_reversal_join_to_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("status", "eq", "Reversed")]
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Equal(2, CountOccurrences(sql, "RII_STOCK_MOVEMENT_OPERATION"));
    }

    private static IQueryable<StockMovementGridRow> BuildQuery(WmsDbContext db, PagedRequest request) =>
        StockMovementService.BuildPagedQuery(request, db.Set<StockMovementOperation>(), db.Set<StockMovementEntry>());

    private static IQueryable<long> BuildCountQuery(WmsDbContext db, PagedRequest request) =>
        StockMovementService.BuildCountQuery(request, db.Set<StockMovementOperation>(), db.Set<StockMovementEntry>());

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
