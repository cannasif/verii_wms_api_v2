using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.Production.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class ProductionSourceWarehouseBalanceTests
{
    [Fact]
    public void Balance_snapshot_matches_stock_configuration_and_unit()
    {
        var prepared = CreatePrepared(
        [
            Material(10, null, "AD"),
            Material(10, 77, "AD"),
            Material(20, null, "ADET"),
            Material(null, null, "AD")
        ]);
        var balances = new ProductionService.ProductionSourceWarehouseBalance[]
        {
            new(10, null, "AD", 10, 3, 7),
            new(10, null, "ad", 2, 1, 1),
            new(10, 77, "AD", 5, 0, 5),
            new(30, 88, "AD", 99, 0, 99)
        };

        var result = ProductionService.ApplySourceWarehouseBalances(prepared, balances);

        Assert.Collection(result.Materials,
            material =>
            {
                Assert.Equal(17, material.SourceWarehouseQuantity);
                Assert.Equal(4, material.SourceWarehouseReservedQuantity);
                Assert.Equal(13, material.SourceWarehouseAvailableQuantity);
            },
            material =>
            {
                Assert.Equal(5, material.SourceWarehouseQuantity);
                Assert.Equal(0, material.SourceWarehouseReservedQuantity);
                Assert.Equal(5, material.SourceWarehouseAvailableQuantity);
            },
            material =>
            {
                Assert.Equal(0, material.SourceWarehouseQuantity);
                Assert.Equal(0, material.SourceWarehouseReservedQuantity);
                Assert.Equal(0, material.SourceWarehouseAvailableQuantity);
            },
            material =>
            {
                Assert.Null(material.SourceWarehouseQuantity);
                Assert.Null(material.SourceWarehouseReservedQuantity);
                Assert.Null(material.SourceWarehouseAvailableQuantity);
            });
    }

    [Fact]
    public void Prepare_contract_loads_all_recipe_balances_with_one_grouped_query()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var serviceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Modules",
            "Production",
            "Application",
            "ProductionService.cs"));

        Assert.Contains("stockIds.Contains(balance.StockId)", serviceSource, StringComparison.Ordinal);
        Assert.Contains(
            ".GroupBy(balance => new { balance.StockId, balance.YapCodeId, balance.UnitCode })",
            serviceSource,
            StringComparison.Ordinal);
        Assert.Contains("AttachSourceWarehouseBalancesAsync(prepared, ct)", serviceSource, StringComparison.Ordinal);
    }

    private static PreparedNetsisProductionMaterial Material(long? stockId, long? yapCodeId, string unitCode) =>
        new(stockId, stockId.HasValue ? $"STK-{stockId}" : "UNMAPPED", null, unitCode, yapCodeId, null,
            10, 1, 0, 1, stockId.HasValue ? null : "Eşleme yok");

    private static PreparedNetsisProductionWorkOrder CreatePrepared(
        IReadOnlyList<PreparedNetsisProductionMaterial> materials) =>
        new(
            ProductionOrderSourceType.NetsisErpFunctions,
            "NETSIS",
            "WO-1",
            1,
            "MAMUL-1",
            "Mamul",
            "AD",
            1,
            100,
            null,
            null,
            7,
            700,
            "Kaynak depo",
            8,
            800,
            "Üretim depo",
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            [],
            materials,
            []);
}
