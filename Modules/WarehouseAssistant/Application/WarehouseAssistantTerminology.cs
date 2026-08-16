namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

internal static class WarehouseAssistantTerminology
{
    public static readonly string[] InstructionWords = ["nasil", "nerede", "hangi menu", "hangi ekran", "ekrani nerede", "yolu nedir"];
    public static readonly string[] WarehouseWords = ["depo", "depolar", "warehouse"];
    public static readonly string[] LocationWords = ["lokasyon", "raf", "hucre", "adres", "location", "bin"];
    public static readonly string[] InventoryCountWords = ["sayim", "envanter sayimi", "inventory count"];
    public static readonly string[] GeneratorProductionWords = ["jenerator", "generator production", "generator project"];
    public static readonly string[] StockInsightWords = ["stoku olmayan", "stok olmayan", "stoku sifir", "sifir stok", "en fazla stok", "en az stok", "en az kullanilabilir", "stoklari karsilastir", "kritik stok"];

    public static readonly string[] WriteCommands =
    [
        "sil", "siler misin", "siliniz", "ekle", "ekler misin", "olustur", "olusturur musun",
        "guncelle", "degistir", "kaydet", "onayla", "iptal et", "iptal etsin", "erp ye gonder",
        "netsise gonder", "aktar", "baslat", "bitir", "tamamla", "kapat", "rezerve et",
        "duzelt", "kaldir", "ata", "gorevlendir", "mail at", "mail gonder",
        "irsaliye kes", "irsaliye olustur", "sevk et", "rafa koy", "stoktan dus", "geri al"
    ];

    public static readonly string[] TurkishSuffixes =
    [
        "larini", "lerini", "larina", "lerine", "larimizda", "lerimizde",
        "larinda", "lerinde", "larindan", "lerinden", "larinin", "lerinin", "larimiz", "lerimiz",
        "sinden", "sindan", "sundan", "sinin", "sunun", "isine", "sine", "sina", "suna",
        "ini", "ını", "unu", "ünü", "ine", "ına", "una", "üne", "ni", "nı", "nu", "nü",
        "dan", "den", "tan", "ten", "nda", "nde", "daki", "deki", "taki", "teki",
        "lari", "leri", "lar", "ler", "nin", "nun", "nın", "nün", "in", "un", "ın", "ün",
        "dir", "dur", "dır", "dür", "tir", "tur", "tır", "tür", "mis", "mus", "mış", "müş",
        "yor", "acak", "ecek", "ildi", "uldu", "üldü", "di", "ti", "du", "tu",
        "im", "um", "ım", "üm", "imiz", "umuz", "ımız", "ümüz", "si", "su", "sı", "sü",
        "da", "de", "ta", "te", "ya", "ye", "yi", "yu", "yı", "yü"
    ];
}
