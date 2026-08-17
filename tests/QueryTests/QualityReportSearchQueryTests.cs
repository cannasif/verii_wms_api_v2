using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityReportSearchQueryTests
{
    [Fact]
    public void Header_search_keeps_all_summary_relations_out_of_paged_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "GKK", SearchFields = ["inspectionNo"] };

        var sql = BuildQuery(db, request, false, false, false).ToQueryString();
        var countSql = BuildQuery(db, request, false, false, false,
                includeReceiptDetails: false, includeWarehouseDetails: false)
            .Select(row => row.Id)
            .ToQueryString();

        Assert.DoesNotContain("RII_QUALITY_INSPECTION_LINES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_QUALITY_INSPECTION_CONTROLS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_QUALITY_INSPECTION_IMAGES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GR_HEADER", countSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_WAREHOUSE", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_search_uses_one_grouped_source()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "1", SearchFields = ["lineCount"] };

        var sql = BuildQuery(db, request, true, false, false).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "RII_QUALITY_INSPECTION_LINES"));
        Assert.DoesNotContain("RII_QUALITY_INSPECTION_CONTROLS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_QUALITY_INSPECTION_IMAGES", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_summary_sort_is_not_required_by_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { SortBy = "totalQuantity", SortDirection = "desc" };

        var itemSql = BuildQuery(db, request, true, false, false, applySort: true).ToQueryString();
        var countSql = BuildQuery(db, request, false, false, false, applySort: false)
            .Select(row => row.Id)
            .ToQueryString();

        Assert.Equal(1, CountOccurrences(itemSql, "RII_QUALITY_INSPECTION_LINES"));
        Assert.DoesNotContain("RII_QUALITY_INSPECTION_LINES", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Control_search_adds_only_control_summary()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "1", SearchFields = ["controlCount"] };

        var sql = BuildQuery(db, request, false, true, false).ToQueryString();

        Assert.Equal(1, CountOccurrences(sql, "RII_QUALITY_INSPECTION_CONTROLS"));
        Assert.DoesNotContain("RII_QUALITY_INSPECTION_LINES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_QUALITY_INSPECTION_IMAGES", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<QualityInspectionReportRow> BuildQuery(
        WmsDbContext db,
        PagedRequest request,
        bool includeLineSummary,
        bool includeControlSummary,
        bool includeImageSummary,
        bool applySort = false,
        bool includeReceiptDetails = true,
        bool includeWarehouseDetails = true) =>
        QualityReportService.BuildInspectionReportQuery(
            request,
            includeReceiptDetails,
            includeWarehouseDetails,
            includeLineSummary,
            includeControlSummary,
            includeImageSummary,
            db.Set<QualityInspection>(),
            db.Set<QualityInspectionLine>(),
            db.Set<QualityInspectionControl>(),
            db.Set<QualityInspectionImage>(),
            db.Set<GoodsReceiptHeader>(),
            db.Set<Warehouse>(),
            applySort);

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
