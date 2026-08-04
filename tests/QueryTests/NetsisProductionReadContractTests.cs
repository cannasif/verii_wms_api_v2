using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using verii_wms_api_v2.Migrations;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
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
    public void Warehouse_return_location_bridge_removes_the_second_set_null_path()
    {
        var foreignKey = new PrepareWarehouseTransferReturnLocation().UpOperations
            .OfType<AddForeignKeyOperation>()
            .Single();

        Assert.Equal(ReferentialAction.NoAction, foreignKey.OnDelete);
        Assert.Equal("DefaultGoodsReceiptLocationId", foreignKey.Columns.Single());
    }
}
