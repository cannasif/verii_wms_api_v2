using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProductionPlanSearchQueryTests
{
    [Fact]
    public void Header_search_keeps_all_summary_tables_out_of_main_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "PR-",
            SearchFields = ["documentNo"]
        };

        var sql = BuildQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_PR_ORDER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PR_MATERIAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PR_OUTPUT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Order_count_search_only_adds_the_order_source()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "2",
            SearchFields = ["orderCount"]
        };

        var sql = BuildQuery(db, request).ToQueryString();

        Assert.Contains("RII_PR_ORDER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PR_MATERIAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PR_OUTPUT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Order_summary_sort_keeps_all_summaries_out_of_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            SortBy = "plannedQuantity",
            SortDirection = "desc"
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_PR_ORDER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PR_MATERIAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PR_OUTPUT", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Material_filter_adds_materials_without_outputs_to_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("materialCount", "gt", "0")]
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("RII_PR_MATERIAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PR_OUTPUT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<ProductionPlanGridRow> BuildQuery(WmsDbContext db, PagedRequest request) =>
        ProductionService.BuildPagedQuery(
            request,
            db.Set<ProductionHeader>(),
            db.Set<ProductionOrder>(),
            db.Set<ProductionMaterialRequirement>(),
            db.Set<ProductionOutputExpectation>());

    private static IQueryable<long> BuildCountQuery(WmsDbContext db, PagedRequest request) =>
        ProductionService.BuildCountQuery(
            request,
            db.Set<ProductionHeader>(),
            db.Set<ProductionOrder>(),
            db.Set<ProductionMaterialRequirement>(),
            db.Set<ProductionOutputExpectation>());

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }
}
