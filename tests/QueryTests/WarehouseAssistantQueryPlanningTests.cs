using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseAssistantQueryPlanningTests
{
    private readonly LocalHybridWarehouseAssistantIntentResolver resolver = new(
        new WarehouseAssistantIntentResolver(),
        Options.Create(new WarehouseAssistantOptions()),
        NullLogger<LocalHybridWarehouseAssistantIntentResolver>.Instance);

    [Theory]
    [InlineData("  İPTAL\tedilen,   SAYIMLARI! ", "iptal edilen sayimlari")]
    [InlineData("A01/R01-G01 lokasyonu", "a01/r01-g01 lokasyonu")]
    [InlineData("ERP'ye gönder", "erp ye gonder")]
    [InlineData(null, "")]
    public void Normalizes_turkish_text_and_preserves_identifier_separators(string? source, string expected)
    {
        Assert.Equal(expected, WarehouseAssistantTextNormalizer.Normalize(source));
    }

    [Theory]
    [InlineData("Kaç depo var?", WarehouseAssistantIntent.WarehouseOverview, WarehouseAssistantQueryKind.WarehouseCount)]
    [InlineData("A01/R01-G01 boş mu?", WarehouseAssistantIntent.LocationInventory, WarehouseAssistantQueryKind.LocationEmptyCheck)]
    [InlineData("En fazla stoklu 10 ürünü göster", WarehouseAssistantIntent.InventoryInsights, WarehouseAssistantQueryKind.RankedStock)]
    [InlineData("En yüksek 10 sayım farkını göster", WarehouseAssistantIntent.InventoryCountAnalysis, WarehouseAssistantQueryKind.InventoryCountVariance)]
    [InlineData("Eksik jeneratör malzemeleri nelerdir?", WarehouseAssistantIntent.GeneratorProductionAnalysis, WarehouseAssistantQueryKind.ProductionMaterialShortages)]
    [InlineData("Transfer nasıl başlatılır?", WarehouseAssistantIntent.NavigationHelp, WarehouseAssistantQueryKind.Navigation)]
    public async Task Produces_a_closed_query_kind_for_each_new_intent(
        string message,
        WarehouseAssistantIntent expectedIntent,
        WarehouseAssistantQueryKind expectedKind)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal(expectedKind, result.QueryKind);
        Assert.NotNull(result.ReasonCodes);
        Assert.NotEmpty(result.ReasonCodes!);
    }

    [Fact]
    public async Task Extracts_entity_filters_measure_sort_and_bounded_limit()
    {
        var warehouse = await resolver.ResolveAsync("10 numaralı depoda hangi lokasyonlar var?", null);
        var location = await resolver.ResolveAsync("A deposundaki A01 rafında X var mı?", null);
        var ranking = await resolver.ResolveAsync("En az kullanılabilir stoğu olan 500 ürünü göster", null);
        var project = await resolver.ResolveAsync("PRJ-001 jeneratör projesi ne durumda?", null);

        Assert.Equal("10", warehouse.WarehouseQuery);
        Assert.Equal("a", location.WarehouseQuery);
        Assert.Equal("a01", location.LocationQuery);
        Assert.Equal(WarehouseAssistantStockMeasure.Available, ranking.StockMeasure);
        Assert.Equal(WarehouseAssistantSortDirection.QuantityAscending, ranking.Sort);
        Assert.Equal(50, ranking.Limit);
        Assert.Equal("PRJ-001", project.ProjectQuery);
    }

    [Theory]
    [InlineData("WT-2026-001 transferini onayla")]
    [InlineData("01/013 stok miktarını düzelt")]
    public async Task Keeps_write_commands_outside_the_query_plan(string message)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.Equal(WarehouseAssistantQueryKind.None, result.QueryKind);
        Assert.StartsWith("local-policy-write-rejected", result.ProviderMode, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Yeni ürün nasıl eklenir?", "stockCard")]
    [InlineData("Mal kabul nasıl yapılır?", "goodsReceipt")]
    [InlineData("Sayım ekranı nerede?", "inventoryCount")]
    [InlineData("Jeneratör projeleri ekranı nerede?", "generatorProjects")]
    public async Task Treats_how_to_questions_as_navigation_instead_of_mutations(string message, string expectedTopic)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(WarehouseAssistantIntent.NavigationHelp, result.Intent);
        Assert.Equal(expectedTopic, result.NavigationTopic);
        Assert.DoesNotContain("write-rejected", result.ProviderMode, StringComparison.Ordinal);
    }
}
