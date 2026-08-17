using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdDistributionSearchQueryTests
{
    [Fact]
    public void Distribution_page_keeps_line_totals_out_of_main_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { SortBy = "id", SortDirection = "desc" };

        var sql = KkdDistributionService.BuildPagedQuery(request, db.Set<KkdDistribution>()).ToQueryString();

        Assert.Contains("RII_KKD_DISTRIBUTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_KKD_DISTRIBUTION_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selected_employee_field_search_keeps_lines_out_of_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest { Search = "TEST", SearchFields = ["employeeName"] };

        var sql = KkdDistributionService.BuildPagedQuery(request, db.Set<KkdDistribution>()).ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_KKD_DISTRIBUTION_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }
}
