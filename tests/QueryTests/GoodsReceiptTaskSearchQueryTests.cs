using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptTaskSearchQueryTests
{
    [Fact]
    public void Header_search_keeps_line_and_assignment_summaries_out_of_the_main_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "GR-",
            SearchFields = ["taskNo"],
            SortBy = "createdDate",
            SortDirection = "desc"
        };

        var sql = BuildQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_GR_TASK_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_TASK_ASSIGNMENT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Planned_quantity_search_includes_lines_but_not_assignments()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "10",
            SearchFields = ["plannedQuantity"]
        };

        var sql = BuildQuery(db, request).ToQueryString();

        Assert.Contains("RII_GR_TASK_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_TASK_ASSIGNMENT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_sort_keeps_lines_out_of_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            SortBy = "plannedQuantity",
            SortDirection = "desc"
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_GR_TASK_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_TASK_ASSIGNMENT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_filter_is_present_in_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("processedQuantity", "gt", "0")]
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("RII_GR_TASK_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_TASK_ASSIGNMENT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<GoodsReceiptTaskGridRow> BuildQuery(WmsDbContext db, PagedRequest request) =>
        GoodsReceiptTaskService.BuildPagedQuery(
            request,
            db.Set<GoodsReceiptTask>(),
            db.Set<GoodsReceiptHeader>(),
            db.Set<Warehouse>(),
            db.Set<GoodsReceiptTaskLine>(),
            db.Set<GoodsReceiptTaskAssignment>(),
            currentUserId: 1);

    private static IQueryable<long> BuildCountQuery(WmsDbContext db, PagedRequest request) =>
        GoodsReceiptTaskService.BuildCountQuery(
            request,
            db.Set<GoodsReceiptTask>(),
            db.Set<GoodsReceiptHeader>(),
            db.Set<Warehouse>(),
            db.Set<GoodsReceiptTaskLine>(),
            db.Set<GoodsReceiptTaskAssignment>(),
            currentUserId: 1);

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }
}
