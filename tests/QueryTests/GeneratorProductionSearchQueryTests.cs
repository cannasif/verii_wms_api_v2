using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GeneratorProduction.Application;
using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GeneratorProductionSearchQueryTests
{
    [Fact]
    public void Project_page_keeps_operation_aggregates_out_of_the_main_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "GEN", SearchFields = ["projectCode"] };

        var sql = GeneratorProductionService.BuildProjectsQuery(request, db.Set<GeneratorProductionProject>())
            .ToQueryString();

        Assert.Contains("RII_GP_PROJECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GP_OPERATION]", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selected_customer_field_is_applied_without_operation_join()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "TEST", SearchFields = ["customerName"] };

        var sql = GeneratorProductionService.BuildProjectsQuery(request, db.Set<GeneratorProductionProject>())
            .ToQueryString();

        Assert.Contains("CustomerNameSnapshot", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_GP_OPERATION]", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }
}
