using verii_wms_api_v2.Modules.WarehouseAssistant.Application;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class WarehouseAssistantIntentResolverTests
{
    private readonly WarehouseAssistantIntentResolver resolver = new();

    [Theory]
    [InlineData("Bugün yaptığım işlemleri göster", WarehouseAssistantIntent.MyActivities)]
    [InlineData("Herkesin bugün yaptığı hareketler", WarehouseAssistantIntent.UserActivities)]
    [InlineData("DTG-1 seri bakiyesi hangi depo ve raflarda?", WarehouseAssistantIntent.SerialBalance)]
    [InlineData("DTG-1 serisi ne zaman ve kim tarafından içeri alındı?", WarehouseAssistantIntent.SerialReceiptHistory)]
    [InlineData("01/013 stok kodlu ürün hangi raflarda var?", WarehouseAssistantIntent.StockLocationBalance)]
    [InlineData("01/013 malzeme bakiyesi nerede?", WarehouseAssistantIntent.StockLocationBalance)]
    public async Task Resolves_supported_turkish_warehouse_questions(string message, WarehouseAssistantIntent expected)
    {
        var result = await resolver.ResolveAsync(message, null);
        Assert.Equal(expected, result.Intent);
    }

    [Theory]
    [InlineData("DTG-1 seri bakiyesi nerede?", "DTG-1")]
    [InlineData("DTG-1 serisi ne zaman ve kim tarafından içeri alındı?", "DTG-1")]
    [InlineData("Seri no: ABC/2026-0001 ne zaman içeri alındı?", "ABC/2026-0001")]
    [InlineData("Barkod XYZ.0004 hangi rafta?", "XYZ.0004")]
    public async Task Extracts_serial_identifiers_without_treating_intent_words_as_values(string message, string expected)
    {
        var result = await resolver.ResolveAsync(message, null);
        Assert.Equal(expected, result.SerialNo);
    }

    [Fact]
    public async Task Uses_previous_serial_context_for_follow_up_question()
    {
        var context = new WarehouseAssistantContext("DTG-1", 13, "01/013");
        var result = await resolver.ResolveAsync("Bu seri ne zaman içeri alındı?", context);

        Assert.Equal(WarehouseAssistantIntent.SerialReceiptHistory, result.Intent);
        Assert.Equal("DTG-1", result.SerialNo);
    }

    [Theory]
    [InlineData("ürün")]
    [InlineData("URUN")]
    [InlineData("Ürün")]
    [InlineData("malzeme")]
    public void Normalization_supports_turkish_stock_synonyms(string value)
    {
        var normalized = WarehouseAssistantIntentResolver.Normalize(value);
        Assert.True(normalized is "urun" or "malzeme");
    }
}
