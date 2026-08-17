using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Procurement.Application;
using verii_wms_api_v2.Modules.Procurement.Domain;
using verii_wms_api_v2.Shared;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProcurementSearchQueryTests
{
    [Fact]
    public void Quote_header_search_keeps_lines_out_of_item_and_count_queries()
    {
        using var db=SqlServerContext();var request=new PagedRequest{Search="QT",SearchFields=["quoteNo"]};
        var itemSql=QuoteRows(db,request).ToQueryString();var countSql=QuoteCount(db,request).ToQueryString();
        Assert.DoesNotContain("RII_PC_QUOTE_LINE",itemSql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PC_QUOTE_LINE",countSql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Quote_total_sort_uses_one_grouped_source_only_in_item_query()
    {
        using var db=SqlServerContext();var request=new PagedRequest{SortBy="totalAmount",SortDirection="desc"};
        var itemSql=QuoteRows(db,request).ToQueryString();var countSql=QuoteCount(db,request).ToQueryString();
        Assert.Contains("GROUP BY",itemSql,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1,CountOccurrences(itemSql,"RII_PC_QUOTE_LINE"));
        Assert.DoesNotContain("RII_PC_QUOTE_LINE",countSql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Quote_total_search_uses_one_grouped_source_in_count_query()
    {
        using var db=SqlServerContext();var request=new PagedRequest{Search="100",SearchFields=["totalAmount"]};
        var sql=QuoteCount(db,request).ToQueryString();
        Assert.Equal(1,CountOccurrences(sql,"RII_PC_QUOTE_LINE"));
    }

    [Fact]
    public void Quote_due_date_sort_uses_the_same_single_grouped_source()
    {
        using var db=SqlServerContext();var request=new PagedRequest{SortBy="dueDate",SortDirection="asc"};
        var sql=QuoteRows(db,request).ToQueryString();
        Assert.Equal(1,CountOccurrences(sql,"RII_PC_QUOTE_LINE"));
    }

    [Fact]
    public void Order_header_search_keeps_lines_out_of_item_and_count_queries()
    {
        using var db=SqlServerContext();var request=new PagedRequest{Search="PO",SearchFields=["documentNo"]};
        var itemSql=OrderRows(db,request).ToQueryString();var countSql=OrderCount(db,request).ToQueryString();
        Assert.DoesNotContain("RII_PC_ORDER_LINE",itemSql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RII_PC_ORDER_LINE",countSql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Order_line_count_filter_uses_one_grouped_source_in_both_queries()
    {
        using var db=SqlServerContext();var request=new PagedRequest{Filters=[new AdvancedFilterRequest("lineCount","gt","0")]};
        var itemSql=OrderRows(db,request).ToQueryString();var countSql=OrderCount(db,request).ToQueryString();
        Assert.Equal(1,CountOccurrences(itemSql,"RII_PC_ORDER_LINE"));
        Assert.Equal(1,CountOccurrences(countSql,"RII_PC_ORDER_LINE"));
    }

    private static IQueryable<ProcurementGridRow> QuoteRows(WmsDbContext db,PagedRequest request)=>
        ProcurementService.BuildQuoteRows(request,db.Set<ProcurementSupplierQuote>(),db.Set<ProcurementSupplierQuoteLine>());
    private static IQueryable<long> QuoteCount(WmsDbContext db,PagedRequest request)=>
        ProcurementService.BuildQuoteCountQuery(request,db.Set<ProcurementSupplierQuote>(),db.Set<ProcurementSupplierQuoteLine>());
    private static IQueryable<ProcurementGridRow> OrderRows(WmsDbContext db,PagedRequest request)=>
        ProcurementService.BuildOrderRows(request,db.Set<ProcurementPurchaseOrder>(),db.Set<ProcurementSupplierQuote>(),db.Set<ProcurementPurchaseOrderLine>());
    private static IQueryable<long> OrderCount(WmsDbContext db,PagedRequest request)=>
        ProcurementService.BuildOrderCountQuery(request,db.Set<ProcurementPurchaseOrder>(),db.Set<ProcurementSupplierQuote>(),db.Set<ProcurementPurchaseOrderLine>());

    private static WmsDbContext SqlServerContext(){var options=new DbContextOptionsBuilder<WmsDbContext>()
        .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=QueryTranslationOnly;Trusted_Connection=True;").Options;return new WmsDbContext(options);}
    private static int CountOccurrences(string value,string expected)=>value.Split(expected,StringSplitOptions.None).Length-1;
}
