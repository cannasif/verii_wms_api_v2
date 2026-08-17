using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseTransferSearchQueryTests
{
    private static readonly WarehouseTransferBusinessContext[] Contexts =
        [WarehouseTransferBusinessContext.InterWarehouse];

    [Fact]
    public void Header_sort_keeps_lines_out_of_the_main_and_count_queries()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { SortBy = "id", SortDirection = "desc" };

        var itemSql = BuildQuery(db, request).ToQueryString();
        var countSql = BuildCountQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_WT_LINE", itemSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_WT_LINE", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_sort_uses_one_grouped_source_only_in_the_main_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { SortBy = "requestedQuantity", SortDirection = "desc" };

        var itemSql = BuildQuery(db, request).ToQueryString();
        var countSql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("GROUP BY", itemSql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(itemSql, "RII_WT_LINE"));
        Assert.DoesNotContain("RII_WT_LINE", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Header_field_search_keeps_lines_out_of_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "WT-", SearchFields = ["documentNo"] };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_WT_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_USER_DETAILS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Created_by_search_joins_only_the_requested_actor_sources()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "admin", SearchFields = ["createdBy"] };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Equal(1, CountOccurrences(sql, "RII_USERS"));
        Assert.Equal(1, CountOccurrences(sql, "RII_USER_DETAILS"));
        Assert.DoesNotContain("RII_WT_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_filter_uses_one_grouped_source_in_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("lineCount", "gt", "1")]
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "RII_WT_LINE"));
    }

    private static IQueryable<WarehouseTransferGridRow> BuildQuery(WmsDbContext db, PagedRequest request) =>
        WarehouseTransferService.BuildPagedQuery(request, Contexts, db.Set<WarehouseTransferHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(), db.Set<WarehouseTransferLine>(),db.Set<User>(),db.Set<UserDetail>());

    private static IQueryable<long> BuildCountQuery(WmsDbContext db, PagedRequest request) =>
        WarehouseTransferService.BuildCountQuery(request, Contexts, db.Set<WarehouseTransferHeader>(),
            db.Set<Warehouse>().IgnoreQueryFilters(), db.Set<WarehouseTransferLine>(),db.Set<User>(),db.Set<UserDetail>());

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
