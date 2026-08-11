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
    [InlineData("01.08.2026 ile 08.08.2026 arasında ABC carisine kaç mal kabul yapıldı, neler alındı?", WarehouseAssistantIntent.GoodsReceiptAnalysis)]
    [InlineData("Bugün sac mal kabul için kaç araç girdi, plakaları neler?", WarehouseAssistantIntent.SteelVehicleAnalysis)]
    [InlineData("Bu hafta yapılan normal depolar arası transferleri göster", WarehouseAssistantIntent.WarehouseTransferAnalysis)]
    [InlineData("Bu hafta kaç üretime transfer yapıldı?", WarehouseAssistantIntent.WarehouseTransferAnalysis)]
    [InlineData("Bana atanmış açık üretime transfer görevlerini göster", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("Vardiya özetimi ve öncelikli işlerimi göster", WarehouseAssistantIntent.ShiftBrief)]
    [InlineData("Müdahale edilmesi gereken operasyon sorunlarını göster", WarehouseAssistantIntent.OperationalExceptions)]
    [InlineData("DTG-1 serisinin uçtan uca izlenebilirliğini göster", WarehouseAssistantIntent.Traceability)]
    [InlineData("GRI-2026-0001 belgesi neden tamamlanamıyor?", WarehouseAssistantIntent.ProcessBlockers)]
    [InlineData("34 ABC 123 plakasının sac mal kabul geçmişini göster", WarehouseAssistantIntent.SteelVehicleAnalysis)]
    public async Task Resolves_supported_turkish_warehouse_questions(string message, WarehouseAssistantIntent expected)
    {
        var result = await resolver.ResolveAsync(message, null);
        Assert.Equal(expected, result.Intent);
    }

    [Theory]
    [InlineData("Mesaiye başladım, önce neye bakayım?", WarehouseAssistantIntent.ShiftBrief)]
    [InlineData("Bugün beni ne bekliyor?", WarehouseAssistantIntent.ShiftBrief)]
    [InlineData("Depoda ters giden ve acil bakmam gereken neler var?", WarehouseAssistantIntent.OperationalExceptions)]
    [InlineData("GRI-2026-0001 hangi adımda kaldı?", WarehouseAssistantIntent.ProcessBlockers)]
    [InlineData("DTG-1 serisinin başına neler geldi?", WarehouseAssistantIntent.Traceability)]
    [InlineData("Benden beklenen işler neler?", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("Ahmet geçen hafta neyle uğraşmış?", WarehouseAssistantIntent.MyActivities)]
    public async Task Resolves_natural_turkish_phrasings_without_external_ai(
        string message,
        WarehouseAssistantIntent expected)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(expected, result.Intent);
    }

    [Theory]
    [InlineData("Bugün yaptığım işemleri göser", WarehouseAssistantIntent.MyActivities)]
    [InlineData("Bana atanan emrleri gster", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("Üretime giden malzemelerden eksik kalan var mı?", WarehouseAssistantIntent.WarehouseTransferAnalysis)]
    [InlineData("Bu hafta üretime verilen malzemelerde eksik var mı?", WarehouseAssistantIntent.WarehouseTransferAnalysis)]
    [InlineData("01/013 depoda nerelere dağılmış?", WarehouseAssistantIntent.StockLocationBalance)]
    [InlineData("01/013 stokta ne kadar kaldı?", WarehouseAssistantIntent.StockLocationBalance)]
    [InlineData("ABC tedarikçisinden geçen hafta neler gelmiş?", WarehouseAssistantIntent.GoodsReceiptAnalysis)]
    [InlineData("Geçen hafta ABC firmasından içeri ne girmiş?", WarehouseAssistantIntent.GoodsReceiptAnalysis)]
    [InlineData("34 ABC 123 bugün geldi mi?", WarehouseAssistantIntent.SteelVehicleAnalysis)]
    [InlineData("GRI-2026-0001 niye hâlâ bekliyor?", WarehouseAssistantIntent.ProcessBlockers)]
    [InlineData("DTG-1 ilk girişten bugüne hangi adımlardan geçmiş?", WarehouseAssistantIntent.Traceability)]
    [InlineData("DTG-1 hangi işlemlerden geçmiş?", WarehouseAssistantIntent.Traceability)]
    [InlineData("DTG-1 nereden nereye taşınmış?", WarehouseAssistantIntent.StockMovementHistory)]
    [InlineData("GRL-000123 etiketi neyi gösteriyor?", WarehouseAssistantIntent.BarcodeLookup)]
    [InlineData("Şu anda depoda aksayan işler neler?", WarehouseAssistantIntent.OperationalExceptions)]
    [InlineData("Yapmam gereken toplama işleri kaldı mı?", WarehouseAssistantIntent.AssignedTasks)]
    public async Task Resolves_local_natural_language_and_minor_typing_errors(
        string message,
        WarehouseAssistantIntent expected)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(expected, result.Intent);
        Assert.Equal("local-inprocess-v2.5", result.ProviderMode);
    }

    [Theory]
    [InlineData("01/013 stok kodunu güncelle")]
    [InlineData("DTG-1 serisini sil")]
    [InlineData("GRI-2026-0001 belgesini onayla")]
    [InlineData("Transferi ERP'ye gönder")]
    public async Task Refuses_write_commands_in_the_local_read_only_assistant(string message)
    {
        var result = await resolver.ResolveAsync(message, null);

        Assert.Equal(WarehouseAssistantIntent.Unknown, result.Intent);
        Assert.Equal(1m, result.Confidence);
    }

    [Fact]
    public async Task Reuses_last_validated_intent_for_a_date_follow_up()
    {
        var context = new WarehouseAssistantContext(
            null,
            null,
            null,
            LastIntent: WarehouseAssistantIntent.GoodsReceiptAnalysis);

        var result = await resolver.ResolveAsync("Peki geçen hafta?", context);

        Assert.Equal(WarehouseAssistantIntent.GoodsReceiptAnalysis, result.Intent);
        Assert.Equal(WarehouseAssistantDatePreset.LastWeek, result.DatePreset);
        Assert.Equal(0.82m, result.Confidence);
    }

    [Fact]
    public async Task Uses_a_short_clarification_answer_with_the_pending_question()
    {
        var context = new WarehouseAssistantContext(
            null,
            null,
            null,
            PendingQuestion: "Hangi stokun depo ve raf bakiyesini görmek istiyorsunuz?");

        var result = await resolver.ResolveAsync("STK-1", context);

        Assert.Equal(WarehouseAssistantIntent.StockLocationBalance, result.Intent);
        Assert.Contains("STK-1", result.StockQuery);
    }

    [Fact]
    public async Task Keeps_a_selected_user_only_for_an_explicit_follow_up()
    {
        var context = new WarehouseAssistantContext(
            null,
            null,
            null,
            LastIntent: WarehouseAssistantIntent.UserActivities,
            TargetUserQuery: "Ahmet Yılmaz",
            RequestsAllUsers: false);

        var followUp = await resolver.ResolveAsync("Peki dün?", context);
        var newQuestion = await resolver.ResolveAsync("Bugün yaptığım işlemleri göster", context);

        Assert.Equal("Ahmet Yılmaz", followUp.TargetUserQuery);
        Assert.False(followUp.RequestsAllUsers);
        Assert.Null(newQuestion.TargetUserQuery);
        Assert.False(newQuestion.RequestsAllUsers);
    }

    [Fact]
    public async Task Extracts_inclusive_explicit_date_range_for_goods_receipt_analysis()
    {
        var result = await resolver.ResolveAsync(
            "08.08.2026 ile 01.08.2026 arasında ABC carisine yapılan mal kabullerde neler alındı?",
            null);

        Assert.Equal(WarehouseAssistantIntent.GoodsReceiptAnalysis, result.Intent);
        Assert.Equal(new DateOnly(2026, 8, 1), result.DateFrom);
        Assert.Equal(new DateOnly(2026, 8, 8), result.DateTo);
        Assert.NotNull(result.SupplierQuery);
    }

    [Theory]
    [InlineData("Show my activities today", WarehouseAssistantIntent.MyActivities)]
    [InlineData("Where is the serial DTG-1 and what is its balance?", WarehouseAssistantIntent.SerialBalance)]
    [InlineData("Show stock movement history for item 01/013", WarehouseAssistantIntent.StockMovementHistory)]
    [InlineData("Show my assigned open tasks", WarehouseAssistantIntent.AssignedTasks)]
    [InlineData("Lookup barcode GRL-000123", WarehouseAssistantIntent.BarcodeLookup)]
    [InlineData("How many steel receipt vehicles entered today?", WarehouseAssistantIntent.SteelVehicleAnalysis)]
    [InlineData("Show production transfers created this week", WarehouseAssistantIntent.WarehouseTransferAnalysis)]
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
    [InlineData("Show my activities last week", WarehouseAssistantDatePreset.LastWeek)]
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
    public async Task Extracts_vehicle_plate_and_transfer_scope_without_confusing_dates()
    {
        var vehicle = await resolver.ResolveAsync("01.08.2026 ile 08.08.2026 arasında plaka 34 ABC 123 olan sac aracı ne zaman girdi?", null);
        var production = await resolver.ResolveAsync("PT-2026-0007 üretime transfer durumunu göster", null);
        var productionMaterial = await resolver.ResolveAsync("MK202600000049 numaralı üretime transferin durumunu göster", null);
        var warehouse = await resolver.ResolveAsync("WT-2026-0012 normal transfer durumunu göster", null);

        Assert.Equal("34 ABC 123", vehicle.VehiclePlateQuery);
        Assert.True(vehicle.HasExplicitDateFilter);
        Assert.Equal(WarehouseAssistantTransferScope.Production, production.TransferScope);
        Assert.False(production.HasExplicitDateFilter);
        Assert.Equal("PT-2026-0007", production.TransferDocumentQuery);
        Assert.Equal("MK202600000049", productionMaterial.TransferDocumentQuery);
        Assert.Equal(WarehouseAssistantTransferScope.InterWarehouse, warehouse.TransferScope);
        Assert.Equal("WT-2026-0012", warehouse.TransferDocumentQuery);
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
