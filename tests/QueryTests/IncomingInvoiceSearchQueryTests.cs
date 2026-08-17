using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Modules.IncomingInvoice.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class IncomingInvoiceSearchQueryTests
{
    [Fact]
    public void Header_search_keeps_all_summary_tables_out_of_main_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "INV-",
            SearchFields = ["invoiceNo"]
        };

        var sql = BuildQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_INCOMING_INVOICE_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_INCOMING_INVOICE_DOCUMENT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_INCOMING_INVOICE_GR_LINK", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_progress_search_uses_one_grouped_line_source()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Search = "1/2",
            SearchFields = ["lineCount"]
        };

        var sql = BuildQuery(db, request).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(sql, "RII_INCOMING_INVOICE_LINE"));
        Assert.DoesNotContain("RII_INCOMING_INVOICE_DOCUMENT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_INCOMING_INVOICE_GR_LINK", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Line_count_sort_keeps_lines_out_of_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            SortBy = "lineCount",
            SortDirection = "desc"
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.DoesNotContain("RII_INCOMING_INVOICE_LINE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Document_filter_only_adds_the_document_source_to_count_query()
    {
        using var db = SqlServerContext();
        var request = new PagedRequest
        {
            Filters = [new AdvancedFilterRequest("hasPdf", "eq", "true")]
        };

        var sql = BuildCountQuery(db, request).ToQueryString();

        Assert.Contains("RII_INCOMING_INVOICE_DOCUMENT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_INCOMING_INVOICE_LINE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_INCOMING_INVOICE_GR_LINK", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<IncomingInvoiceGridRow> BuildQuery(WmsDbContext db, PagedRequest request) =>
        IncomingInvoiceService.BuildPagedQuery(
            "0",
            request,
            db.Set<IncomingInvoiceHeader>(),
            db.Set<IncomingInvoiceLine>(),
            db.Set<IncomingInvoiceDocument>(),
            db.Set<IncomingInvoiceGoodsReceiptLink>());

    private static IQueryable<long> BuildCountQuery(WmsDbContext db, PagedRequest request) =>
        IncomingInvoiceService.BuildCountQuery(
            "0",
            request,
            db.Set<IncomingInvoiceHeader>(),
            db.Set<IncomingInvoiceLine>(),
            db.Set<IncomingInvoiceDocument>(),
            db.Set<IncomingInvoiceGoodsReceiptLink>());

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
