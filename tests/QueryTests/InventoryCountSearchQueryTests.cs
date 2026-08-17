using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.InventoryCount.Application;
using verii_wms_api_v2.Modules.InventoryCount.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class InventoryCountSearchQueryTests
{
    [Fact]
    public void Header_field_search_does_not_join_actor_tables()
    {
        using var db = SqlServerContext();

        var sql = BuildQuery(db, new PagedRequest
        {
            Search = "COUNT-",
            SearchFields = ["documentNo"]
        }).ToQueryString();

        Assert.Contains("RII_INVENTORY_COUNT_HEADER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_USER_DETAILS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("createdBy")]
    [InlineData("updatedBy")]
    public void Actor_search_joins_actor_tables_only_when_selected(string field)
    {
        using var db = SqlServerContext();

        var sql = BuildQuery(db, new PagedRequest
        {
            Search = "System Administrator",
            SearchFields = [field]
        }).ToQueryString();

        Assert.Contains("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RII_USER_DETAILS", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<InventoryCountGridRow> BuildQuery(
        WmsDbContext db,
        PagedRequest request) =>
        InventoryCountService.BuildGridQuery(
            request,
            db.Set<InventoryCountHeader>(),
            db.Set<Warehouse>(),
            db.Set<User>(),
            db.Set<UserDetail>());

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }
}
