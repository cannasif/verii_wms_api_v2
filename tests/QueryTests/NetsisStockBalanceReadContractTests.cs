using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.NetsisRead.Infrastructure;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class NetsisStockBalanceReadContractTests
{
    [Fact]
    public async Task Empty_filters_read_the_complete_function_result_without_selecting_extra_columns()
    {
        var executor = new CapturingNetsisQueryExecutor();
        var service = new NetsisReadService(executor);

        var result = await service.GetStockBalancesAsync(null, null, CancellationToken.None);

        Assert.Empty(result);
        var query = Assert.Single(executor.Queries);
        Assert.Equal("RII_FN_STOCK_BALANCE", query.Operation);
        Assert.Contains("SELECT DEPO_KODU, STOK_KODU, BAKIYE", query.Sql, StringComparison.Ordinal);
        Assert.Contains("dbo.RII_FN_STOCK_BALANCE(@warehouseCode, @stockCode)", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT *", query.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DBNull.Value, query.Parameters.Single(x => x.ParameterName == "@warehouseCode").Value);
        Assert.Equal(DBNull.Value, query.Parameters.Single(x => x.ParameterName == "@stockCode").Value);
    }

    [Fact]
    public async Task Filters_use_the_database_function_parameter_types_and_normalize_stock_code()
    {
        var executor = new CapturingNetsisQueryExecutor();
        var service = new NetsisReadService(executor);

        await service.GetStockBalancesAsync(7, "  STK-001  ", CancellationToken.None);

        var parameters = Assert.Single(executor.Queries).Parameters;
        var warehouse = Assert.Single(parameters, x => x.ParameterName == "@warehouseCode");
        var stock = Assert.Single(parameters, x => x.ParameterName == "@stockCode");
        Assert.Equal(SqlDbType.Int, warehouse.SqlDbType);
        Assert.Equal((short)7, warehouse.Value);
        Assert.Equal(SqlDbType.VarChar, stock.SqlDbType);
        Assert.Equal(50, stock.Size);
        Assert.Equal("STK-001", stock.Value);
    }

    [Fact]
    public void Dto_keeps_warehouse_nullability_and_decimal_balance_precision()
    {
        var row = new NetsisStockBalanceDto(null, "01/001", 123.12345678m);

        var json = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"warehouseCode\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"stockCode\":\"01/001\"", json, StringComparison.Ordinal);
        Assert.Contains("\"balance\":123.12345678", json, StringComparison.Ordinal);
    }

    private sealed record CapturedQuery(
        string Operation,
        string Sql,
        IReadOnlyList<SqlParameter> Parameters);

    private sealed class CapturingNetsisQueryExecutor : INetsisQueryExecutor
    {
        public List<CapturedQuery> Queries { get; } = [];

        public Task<List<T>> QueryAsync<T>(
            string operation,
            string sql,
            Func<SqlDataReader, T> map,
            CancellationToken cancellationToken,
            params SqlParameter[] parameters)
        {
            Queries.Add(new CapturedQuery(operation, sql, parameters));
            return Task.FromResult(new List<T>());
        }
    }
}
