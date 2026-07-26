using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Localization;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class DocumentSeriesSearchTests
{
    [Fact]
    public async Task Actor_search_translates_to_server_side_sql()
    {
        using var db = SqlServerContext();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor());
        var service = new DocumentSeriesService(
            unitOfWork,
            new NoopAuditLogWriter(),
            new PassThroughLocalizer<DocumentSeriesResource>());
        var request = new PagedRequest
        {
            Search = "System Administrator",
            SearchFields = ["createdBy"]
        };

        var sql = service.BuildPagedQuery(request).ToQueryString();

        Assert.Contains("RII_USERS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RII_USER_DETAILS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("warehouseCode", "1", "WarehouseCode")]
    [InlineData("warehouseName", "Merkez", "WarehouseName")]
    public async Task Warehouse_fields_translate_to_separate_server_side_searches(string field, string search, string expectedColumn)
    {
        using var db = SqlServerContext();
        await using var unitOfWork = new UnitOfWork(db, new HttpContextAccessor());
        var service = new DocumentSeriesService(
            unitOfWork,
            new NoopAuditLogWriter(),
            new PassThroughLocalizer<DocumentSeriesResource>());

        var sql = service.BuildPagedQuery(new PagedRequest
        {
            Search = search,
            SearchFields = [field]
        }).ToQueryString();

        Assert.Contains("RII_WAREHOUSE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedColumn, sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WarehouseSearchText", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static WmsDbContext SqlServerContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;")
            .Options;
        return new WmsDbContext(options);
    }

    private sealed class NoopAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class PassThroughLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, true);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, name, arguments), true);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
