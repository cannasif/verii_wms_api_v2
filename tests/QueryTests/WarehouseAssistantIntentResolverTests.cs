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
    [InlineData("Barkod GRL-000123 hangi stoka ait?", WarehouseAssistantIntent.BarcodeLookup)]
    [InlineData("01/013 stok hareketlerini göster", WarehouseAssistantIntent.StockMovementHistory)]
    [InlineData("Bana atanmış açık görevleri göster", WarehouseAssistantIntent.AssignedTasks)]
    public async Task Resolves_supported_turkish_warehouse_questions(string message, WarehouseAssistantIntent expected)
    {
        var result = await resolver.ResolveAsync(message, null);
        Assert.Equal(expected, result.Intent);
    }

    [Theory]
    [InlineData("Show my activities today", WarehouseAssistantIntent.MyActivities)]
    [InlineData("Where is the serial DTG-1 and what is its balance?", WarehouseAssistantIntent.SerialBalance)]
    [InlineData("Show stock movement history for item 01/013", WarehouseAssistantIntent.StockMovementHistory)]
    [InlineData("Show my assigned open tasks", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("Lookup barcode GRL-000123", WarehouseAssistantIntent.BarcodeLookup)]
    [InlineData("Zeige meine offenen Aufgaben", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("Afficher mes tâches ouvertes", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("Mostrar mis tareas abiertas", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("Mostra i miei compiti aperti", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("اعرض مهامي المفتوحة", WarehouseAssistantIntent.AssignedTasks)]
    public async Task Resolves_supported_questions_without_external_ai(
        string message,
        WarehouseAssistantIntent expected)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(expected, result.Intent);
    }

    [Theory]
    [InlineData("Show my activities yesterday", WarehouseAssistantDatePreset.Yesterday)]
    [InlineData("Zeige meine Aktivitäten diese Woche", WarehouseAssistantDatePreset.ThisWeek)]
    [InlineData("Afficher les opérations des 7 derniers jours", WarehouseAssistantDatePreset.LastSevenDays)]
    [InlineData("Mostrar operaciones de los últimos 30 días", WarehouseAssistantDatePreset.LastThirtyDays)]
    public async Task Resolves_multilingual_date_ranges(
        string message,
        WarehouseAssistantDatePreset expected)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(expected, result.DatePreset);
    }

    [Theory]
    [InlineData("Barkod GRL-000123 hangi stoka ait?", "GRL-000123")]
    [InlineData("Etiket: '01086900000000011726010110LOT-1' sorgula", "01086900000000011726010110LOT-1")]
    [InlineData("Lookup barcode GRL-000123", "GRL-000123")]
    [InlineData("Barcode: '01086900000000011726010110LOT-1'", "01086900000000011726010110LOT-1")]
    public async Task Extracts_barcode_value_for_safe_central_resolution(string message, string expected)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(WarehouseAssistantIntent.BarcodeLookup, result.Intent);
        Assert.Equal(expected, result.Barcode);
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
