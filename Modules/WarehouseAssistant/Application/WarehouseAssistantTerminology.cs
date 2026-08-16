namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

internal static class WarehouseAssistantTerminology
{
    public static readonly string[] InstructionWords =
    [
        "nasil", "nerede", "nerden", "nereden", "nereden acilir", "nereden acacagim",
        "hangi menu", "hangi ekran", "hangi sayfa", "sayfasi", "sayfa", "ekrani nerede",
        "nereden bakilir", "nereden bulurum", "nasil giderim", "yolu nedir"
    ];
    public static readonly string[] WarehouseWords = ["depo", "depolar", "ambar", "ambarlar", "warehouse"];
    public static readonly string[] LocationWords =
    ["lokasyon", "raf", "raf gozu", "goz", "hucre", "bolme", "adres", "location", "bin"];
    public static readonly string[] InventoryCountWords = ["sayim", "envanter sayimi", "inventory count"];
    public static readonly string[] GeneratorProductionWords = ["jenerator", "generator production", "generator project"];
    public static readonly string[] StockInsightWords =
    [
        "stoku olmayan", "stok olmayan", "stoku sifir", "sifir stok", "hic kalmayan",
        "hic olmayan urun", "elde hic olmayan", "stokta hic olmayan", "mevcudu olmayan", "stogu bitmis",
        "en fazla stok", "en yuksek stok", "en cok bulunan", "en cok hangi", "en cok mal", "en az stok",
        "en dusuk", "en az kullanilabilir", "kullanabilecegimiz en az",
        "stoklari karsilastir", "mallari kiyasla", "kiyasla", "mukayese", "yan yana goster",
        "kritik stok", "riskli seviye", "asgari stok", "minimum stok"
    ];

    public static readonly string[] WriteCommands =
    [
        "sil", "siler misin", "siliniz", "ekle", "ekler misin", "olustur", "olusturur musun",
        "silebilir misin", "guncelle", "guncelleyebilir misin", "degistir", "degistirebilir misin",
        "kaydet", "kaydedebilir misin", "onayla", "onaylayiver", "onaylar misin", "onaylayabilir misin",
        "iptal et", "iptal etsin", "iptal eder misin", "iptal edebilir misin", "erp ye gonder",
        "netsise gonder", "aktar", "baslat", "bitir", "tamamla", "kapat", "rezerve et",
        "duzelt", "kaldir", "ata", "gorevlendir", "mail at", "mail gonder",
        "irsaliye kes", "irsaliye olustur", "sevk et", "rafa koy", "stoktan dus", "geri al"
    ];

    public static readonly string[] TurkishSuffixes =
    [
        "larini", "lerini", "larina", "lerine", "larimizda", "lerimizde",
        "larinda", "lerinde", "larindan", "lerinden", "larinin", "lerinin", "larin", "lerin", "larimiz", "lerimiz",
        "larda", "lerde",
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
