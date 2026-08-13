using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using verii_wms_api_v2.Migrations;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.NetsisRead.Infrastructure;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class NetsisProductionReadContractTests
{
    [Fact]
    public void Migration_installs_the_three_production_read_functions_without_touching_erp_data()
    {
        var sql = new AddNetsisProductionReadFunctions().UpOperations
            .OfType<SqlOperation>()
            .Select(x => x.Sql)
            .ToList();

        Assert.Equal(3, sql.Count);
        Assert.Contains(sql, x => x.Contains("RII_FN_ISEMRI", StringComparison.Ordinal));
        Assert.Contains(sql, x => x.Contains("RII_FN_STOK_RECETE", StringComparison.Ordinal));
        Assert.Contains(sql, x => x.Contains("RII_FN_ISEMRI_RECETE", StringComparison.Ordinal));
        Assert.Contains(sql, x => x.Contains("I.SUBEKODU", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, x => x.Contains("I.SUBE_KODU", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, x => x.Contains("I.OLCUBR", StringComparison.Ordinal));
        Assert.Equal(2, sql.Count(x => x.Contains("V3RIICO", StringComparison.Ordinal)));
        Assert.DoesNotContain(sql, x =>
            x.Contains("INSERT ", StringComparison.OrdinalIgnoreCase)
            || x.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
            || x.Contains("DELETE ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Work_order_recipe_contract_exposes_units_recipe_total_and_calculated_totals()
    {
        var row = new ProductionWorkOrderRecipeComponentDto(
            "IE-1", 0, "MAMUL", "Mamul", null, 10m, "ADET", 1m,
            "HAM", "Hammadde", "KG", null, 10, 2m, 2m, 0.2m, 1m,
            false, 20m, 4m, 21m);

        var json = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"productUnitCode\":\"ADET\"", json, StringComparison.Ordinal);
        Assert.Contains("\"componentUnitCode\":\"KG\"", json, StringComparison.Ordinal);
        Assert.Contains("\"recipeTotal\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"baseRequiredQuantity\":20", json, StringComparison.Ordinal);
        Assert.Contains("\"variableWasteQuantity\":4", json, StringComparison.Ordinal);
        Assert.Contains("\"totalRequiredQuantity\":21", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Compatible_production_functions_match_the_installed_netsis_schema_and_include_waste()
    {
        var sql = new AddCompatibleNetsisProductionReadFunctions().UpOperations
            .OfType<SqlOperation>()
            .Select(x => x.Sql)
            .ToList();

        Assert.Equal(3, sql.Count);
        Assert.Contains(sql, x => x.Contains("I.SUBEKODU", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, x => x.Contains("I.SUBE_KODU", StringComparison.Ordinal));
        Assert.DoesNotContain(sql, x => x.Contains("I.OLCUBR", StringComparison.Ordinal));
        Assert.Contains(sql, x => x.Contains(
            "B.BazIhtiyacMiktari + F.DegiskenFireMiktari + R.SabitFireMiktari",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Production_work_order_description_migration_adds_source_column_and_projects_netsis_description()
    {
        var migration = new AddProductionWorkOrderDescription();
        var addColumn = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;

        Assert.Equal("RII_PR_SOURCE_ORDER", addColumn.Table);
        Assert.Equal("Description", addColumn.Name);
        Assert.Equal(1000, addColumn.MaxLength);
        Assert.True(addColumn.IsNullable);
        Assert.Contains("I.ACIKLAMA", sql, StringComparison.Ordinal);
        Assert.Contains("AS Aciklama", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_work_order_contract_serializes_description()
    {
        var row = new ProductionWorkOrderDto(
            "IE-1", 1, "MAMUL", "Mamul", null, 10m, 1, "ADET", 1m,
            DateTime.UtcNow, null, null, 0, null, 1, 2, false, "Üretim notu");

        var json = JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var document = JsonDocument.Parse(json);
        Assert.Equal("Üretim notu", document.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void Warehouse_return_location_bridge_removes_the_second_set_null_path()
    {
        var foreignKey = new PrepareWarehouseTransferReturnLocation().UpOperations
            .OfType<AddForeignKeyOperation>()
            .Single();

        Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete);
        Assert.Equal("DefaultGoodsReceiptLocationId", foreignKey.Columns.Single());
    }

    [Fact]
    public async Task Work_order_recipes_are_read_in_one_parameterized_round_trip_for_the_list_page()
    {
        var executor = new CapturingNetsisQueryExecutor();
        var service = new NetsisReadService(executor);
        var workOrders = Enumerable.Range(1, 200)
            .Select(index => $"IE-{index:0000}")
            .Append("  ie-0001  ")
            .ToArray();

        var result = await service.GetProductionWorkOrderRecipesAsync(workOrders, 1, CancellationToken.None);

        Assert.Empty(result);
        var query = Assert.Single(executor.Queries);
        Assert.Equal("RII_FN_ISEMRI_RECETE_BATCH", query.Operation);
        Assert.Contains("CROSS APPLY dbo.RII_FN_ISEMRI_RECETE", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IE-0001", query.Sql, StringComparison.Ordinal);
        Assert.Equal(201, query.Parameters.Count);
        Assert.Equal(200, query.Parameters.Count(parameter => parameter.ParameterName.StartsWith("@workOrderNumber", StringComparison.Ordinal)));
        Assert.Single(query.Parameters, parameter => parameter.ParameterName == "@branchCode");
    }

    [Fact]
    public async Task Work_order_recipe_batching_stays_below_the_sql_server_parameter_limit()
    {
        var executor = new CapturingNetsisQueryExecutor();
        var service = new NetsisReadService(executor);
        var workOrders = Enumerable.Range(1, 501).Select(index => $"IE-{index:0000}").ToArray();

        await service.GetProductionWorkOrderRecipesAsync(workOrders, 1, CancellationToken.None);

        Assert.Equal(2, executor.Queries.Count);
        Assert.Equal(501, executor.Queries.Sum(query =>
            query.Parameters.Count(parameter => parameter.ParameterName.StartsWith("@workOrderNumber", StringComparison.Ordinal))));
        Assert.All(executor.Queries, query => Assert.True(query.Parameters.Count <= 501));
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
