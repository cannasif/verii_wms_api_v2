namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

internal sealed record WarehouseAssistantIntentExample(WarehouseAssistantIntent Intent, string Text);

/// <summary>
/// Versioned, reviewable examples contain language only. Live warehouse facts and user data
/// must never be added to this catalog.
/// </summary>
internal static class WarehouseAssistantIntentCatalog
{
    public static IReadOnlyList<WarehouseAssistantIntentExample> Examples { get; } =
    [
        E(WarehouseAssistantIntent.Help, "Bu asistana hangi depo sorularını sorabilirim?"),
        E(WarehouseAssistantIntent.Help, "Yapabildiğin işlemleri ve örnek soruları göster."),
        E(WarehouseAssistantIntent.Help, "What warehouse questions can I ask?"),

        E(WarehouseAssistantIntent.MyActivities, "Bugün depoda yaptığım işlemleri göster."),
        E(WarehouseAssistantIntent.MyActivities, "Dün hangi operasyon kayıtlarını ben oluşturdum?"),
        E(WarehouseAssistantIntent.MyActivities, "Show the warehouse operations I performed today."),

        E(WarehouseAssistantIntent.UserActivities, "Ahmet kullanıcısının bugün yaptığı depo işlemlerini göster."),
        E(WarehouseAssistantIntent.UserActivities, "Ekipteki herkesin bu haftaki operasyonlarını listele."),
        E(WarehouseAssistantIntent.UserActivities, "Show activities performed by all warehouse users."),

        E(WarehouseAssistantIntent.SerialBalance, "Bu seri hangi depo ve raflarda kaç adet bulunuyor?"),
        E(WarehouseAssistantIntent.SerialBalance, "Seri numarasının kullanılabilir bakiyesini ve konumunu göster."),
        E(WarehouseAssistantIntent.SerialBalance, "Where is this serial and how much is available?"),

        E(WarehouseAssistantIntent.SerialReceiptHistory, "Bu seri ne zaman ve kim tarafından mal kabul edildi?"),
        E(WarehouseAssistantIntent.SerialReceiptHistory, "Serinin ilk depo girişini ve teslim alan personeli göster."),
        E(WarehouseAssistantIntent.SerialReceiptHistory, "When was this serial received and by whom?"),

        E(WarehouseAssistantIntent.StockLocationBalance, "Bu malzeme hangi depo ve raflarda ne kadar bulunuyor?"),
        E(WarehouseAssistantIntent.StockLocationBalance, "Ürünün kullanılabilir, rezerve ve toplam raf bakiyesini göster."),
        E(WarehouseAssistantIntent.StockLocationBalance, "Bu numaralı parça deponun tam olarak neresinde duruyor?"),
        E(WarehouseAssistantIntent.StockLocationBalance, "Aradığım stok hangi lokasyonda ve elimizde kaç tane var?"),
        E(WarehouseAssistantIntent.StockLocationBalance, "Where is this item stored and what is its balance?"),

        E(WarehouseAssistantIntent.BarcodeLookup, "Okuttuğum etiketi çözümle ve hangi stoka ait olduğunu göster."),
        E(WarehouseAssistantIntent.BarcodeLookup, "Bu barkodun stok, seri, lot ve miktar bilgilerini getir."),
        E(WarehouseAssistantIntent.BarcodeLookup, "Decode this warehouse label and identify the item."),

        E(WarehouseAssistantIntent.StockMovementHistory, "Bu ürünün giriş çıkış hareket geçmişini göster."),
        E(WarehouseAssistantIntent.StockMovementHistory, "Seri hangi raflardan geçmiş ve hangi belgelerle taşınmış?"),
        E(WarehouseAssistantIntent.StockMovementHistory, "Show the inbound and outbound movement ledger for this item."),

        E(WarehouseAssistantIntent.AssignedTasks, "Bana atanmış açık toplama emirlerini göster."),
        E(WarehouseAssistantIntent.AssignedTasks, "Sıradaki işlerimi ve tamamlanmamış görevlerimi listele."),
        E(WarehouseAssistantIntent.AssignedTasks, "Show my assigned open warehouse tasks."),

        E(WarehouseAssistantIntent.GoodsReceiptAnalysis, "Bu cariden tarih aralığında kaç mal kabul yapıldı ve neler alındı?"),
        E(WarehouseAssistantIntent.GoodsReceiptAnalysis, "Tedarikçiye göre gelen ürünleri ve kabul miktarlarını raporla."),
        E(WarehouseAssistantIntent.GoodsReceiptAnalysis, "Geçtiğimiz ay bu firmadan depoya ulaşan malzemeleri özetle."),
        E(WarehouseAssistantIntent.GoodsReceiptAnalysis, "Belirtilen cari tarafından gönderilen hangi ürünler kabul edildi?"),
        E(WarehouseAssistantIntent.GoodsReceiptAnalysis, "Bu satıcıdan depoya gelenleri tarih ve miktarlarıyla göster."),
        E(WarehouseAssistantIntent.GoodsReceiptAnalysis, "Report goods receipts and received items by supplier and date."),

        E(WarehouseAssistantIntent.SteelVehicleAnalysis, "Bugün sac mal kabul için kaç araç geldi ve plakaları neler?"),
        E(WarehouseAssistantIntent.SteelVehicleAnalysis, "Bu plakalı aracın levha kabul geçmişini göster."),
        E(WarehouseAssistantIntent.SteelVehicleAnalysis, "Show steel receipt vehicle arrivals and accepted plate counts."),

        E(WarehouseAssistantIntent.WarehouseTransferAnalysis, "Üretime transferlerin durumunu ve eksik kalan miktarları göster."),
        E(WarehouseAssistantIntent.WarehouseTransferAnalysis, "Depolar arası transferlerden bekleyenleri listele."),
        E(WarehouseAssistantIntent.WarehouseTransferAnalysis, "Üretime gönderilen malzemelerden hangileri yarım kaldı?"),
        E(WarehouseAssistantIntent.WarehouseTransferAnalysis, "Üretim besleme transferlerinde talep edilenden eksik verilenleri göster."),
        E(WarehouseAssistantIntent.WarehouseTransferAnalysis, "Kaynak depodan hedef depoya giden tamamlanmamış transferleri bul."),
        E(WarehouseAssistantIntent.WarehouseTransferAnalysis, "Show pending inter-warehouse and production transfers."),

        E(WarehouseAssistantIntent.ShiftBrief, "Vardiyaya başlıyorum, öncelikli işlerimi özetle."),
        E(WarehouseAssistantIntent.ShiftBrief, "Bugün önce hangi depo işlerine bakmalıyım?"),
        E(WarehouseAssistantIntent.ShiftBrief, "Masanın başına geldim, operasyonel olarak odağımı nereye vermeliyim?"),
        E(WarehouseAssistantIntent.ShiftBrief, "İşe yeni başladım, en acil işten başlayarak bana yol göster."),
        E(WarehouseAssistantIntent.ShiftBrief, "Vardiyada önümdeki işleri önem sırasına koy."),
        E(WarehouseAssistantIntent.ShiftBrief, "Give me a prioritized warehouse shift brief."),

        E(WarehouseAssistantIntent.OperationalExceptions, "Müdahale gerektiren gecikmiş ve başarısız operasyonları göster."),
        E(WarehouseAssistantIntent.OperationalExceptions, "ERP aktarımı başarısız, kalitede bekleyen veya takılan işleri listele."),
        E(WarehouseAssistantIntent.OperationalExceptions, "Show critical warehouse exceptions and failed integrations."),

        E(WarehouseAssistantIntent.Traceability, "Bu serinin ilk girişten bugüne uçtan uca yolculuğunu göster."),
        E(WarehouseAssistantIntent.Traceability, "Barkod hangi kabul, transfer, raf ve sevk işlemlerinden geçti?"),
        E(WarehouseAssistantIntent.Traceability, "Show the end-to-end traceability journey of this serial."),

        E(WarehouseAssistantIntent.ProcessBlockers, "Bu belge neden tamamlanamıyor ve hangi adımda bekliyor?"),
        E(WarehouseAssistantIntent.ProcessBlockers, "Emrin ilerlemesini engelleyen kalite, stok veya ERP sorununu açıkla."),
        E(WarehouseAssistantIntent.ProcessBlockers, "Explain why this warehouse document is blocked."),

        E(WarehouseAssistantIntent.ParameterHelp, "Bu depo parametresi açılırsa süreçte ne değişir?"),
        E(WarehouseAssistantIntent.ParameterHelp, "Seçtiğim ayarın etkilediği ekranları ve örnek senaryoyu açıkla."),
        E(WarehouseAssistantIntent.ParameterHelp, "Explain what this WMS setting changes in the workflow.")
    ];

    private static WarehouseAssistantIntentExample E(WarehouseAssistantIntent intent, string text) => new(intent, text);
}
