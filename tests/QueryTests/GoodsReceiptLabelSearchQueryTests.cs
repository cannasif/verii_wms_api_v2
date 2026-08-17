using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptLabelSearchQueryTests
{
    [Fact]
    public void Batch_search_keeps_task_reference_tables_out_of_main_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "LBL-",
            SearchFields = ["batchNo"]
        };

        var sql = BuildQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_GR_LABEL]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_TASK_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_TASK]", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Task_number_search_uses_one_task_reference_path()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "RCV-",
            SearchFields = ["taskNo"]
        };

        var sql = BuildQuery(db, request).ToQueryString();

        Assert.Equal(1, CountOccurrences(sql, "RII_GR_LABEL]"));
        Assert.Equal(1, CountOccurrences(sql, "RII_GR_TASK_LINE"));
        Assert.Equal(1, CountOccurrences(sql, "RII_GR_TASK]"));
    }

    [Fact]
    public void Task_number_sort_keeps_reference_tables_out_of_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            SortBy = "taskNo",
            SortDirection = "asc"
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_GR_LABEL]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_TASK_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<GoodsReceiptLabelBatchRow> BuildQuery(WmsDbContext db, PagedRequest request) =>
        GoodsReceiptLabelService.BuildPagedQuery(
            request,
            db.Set<GoodsReceiptLabelBatch>(),
            db.Set<GoodsReceiptHeader>(),
            db.Set<GoodsReceiptLabel>(),
            db.Set<GoodsReceiptTaskLine>(),
            db.Set<GoodsReceiptTask>());

    private static IQueryable<long> BuildCountQuery(WmsDbContext db, PagedRequest request) =>
        GoodsReceiptLabelService.BuildCountQuery(
            request,
            db.Set<GoodsReceiptLabelBatch>(),
            db.Set<GoodsReceiptHeader>(),
            db.Set<GoodsReceiptLabel>(),
            db.Set<GoodsReceiptTaskLine>(),
            db.Set<GoodsReceiptTask>());

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
