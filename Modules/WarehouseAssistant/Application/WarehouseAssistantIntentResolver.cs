using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed class WarehouseAssistantIntentResolver : IWarehouseAssistantIntentResolver
{
    private static readonly string[] SerialWords =
    [
        "seri", "serial", "seriennummer", "numero de serie", "numero di serie", "رقم تسلسلي", "barkod", "barcode", "etiket", "label"
    ];

    private static readonly string[] StockWords =
    [
        "stok", "urun", "malzeme", "mamul", "parca", "stock", "item", "product", "material",
        "bestand", "artikel", "produkt", "matiere", "produit", "articulo", "producto", "prodotto", "materiale", "مخزون", "صنف", "مادة", "منتج"
    ];

    private static readonly string[] BalanceWords =
    [
        "bakiye", "miktar", "kac", "nerede", "hangi depo", "hangi raf", "lokasyon", "konum",
        "balance", "quantity", "how many", "where", "warehouse", "location", "bin",
        "bestand", "menge", "wo", "lager", "lagerplatz",
        "solde", "quantite", "combien", "ou", "entrepot", "emplacement",
        "saldo", "cantidad", "cuanto", "donde", "almacen", "ubicacion",
        "giacenza", "quantita", "quanto", "dove", "magazzino", "ubicazione",
        "رصيد", "كمية", "اين", "مستودع", "موقع", "رف"
    ];

    private static readonly string[] ReceiptWords =
    [
        "mal kabul", "irsaliye", "iceri", "giris", "alindi", "kabul edildi", "ne zaman", "kim tarafindan", "kim aldi",
        "goods receipt", "received", "receipt", "inbound", "when", "who",
        "wareneingang", "eingang", "angenommen", "wann", "wer",
        "reception", "recu", "entree", "quand", "qui",
        "recepcion", "recibido", "entrada", "cuando", "quien",
        "ricevimento", "ricevuto", "ingresso", "quando", "chi",
        "استلام", "استلام بضاعة", "تم الاستلام", "متى", "من"
    ];

    private static readonly string[] ActivityWords =
    [
        "islem", "hareket", "yaptigim", "yapmis", "yapti", "aktivit", "kayit",
        "activity", "activities", "actions", "operations", "did today",
        "aktivitat", "aktionen", "vorgange", "activite", "actions", "operations",
        "actividad", "acciones", "operaciones", "attivita", "azioni", "operazioni",
        "نشاط", "عمليات", "اجراءات", "سجل"
    ];

    private static readonly string[] MovementWords =
    [
        "stok hareket", "seri hareket", "malzeme hareket", "urun hareket", "hareket gecmis", "giris cikis", "nereden nereye",
        "stock movement", "serial movement", "item movement", "movement history", "movement trail", "from where to where",
        "bestandsbeweg", "serienbeweg", "bewegungsverlauf", "mouvement de stock", "historique des mouvements",
        "movimiento de stock", "historial de movimientos", "movimento di magazzino", "storico movimenti",
        "حركة المخزون", "حركة الصنف", "سجل الحركات"
    ];

    private static readonly string[] TaskWords =
    [
        "atanan emir", "atanmis emir", "gorevlerim", "gorev list", "bekleyen gorev", "acik gorev", "toplama emri", "is emirlerim", "is emri list",
        "assigned task", "my tasks", "open tasks", "pending tasks", "picking task", "work orders",
        "zugewiesene aufgabe", "meine aufgaben", "offene aufgaben", "meine offenen aufgaben", "kommissionierauftrag",
        "tache assignee", "mes taches", "taches ouvertes", "ordre de preparation",
        "tarea asignada", "mis tareas", "tareas abiertas", "orden de preparacion",
        "attivita assegnata", "i miei compiti", "attivita aperte", "ordine di prelievo",
        "المهام المسندة", "مهامي", "المهام المفتوحة", "امر تجميع"
    ];

    private static readonly string[] BarcodeWords =
    [
        "barkod", "etiket", "barcode", "label", "strichcode", "code barre", "codigo de barras", "codice a barre", "باركود", "رمز شريطي"
    ];

    private static readonly string[] BarcodeLookupWords =
    [
        "barkod sorgula", "barkodu sorgula", "barkod ne", "barkod kime", "barkod hangi", "etiket sorgula", "etiketi sorgula", "etiket ne", "etiket hangi", "barkodu cozumle", "etiketi cozumle",
        "lookup barcode", "scan barcode", "what is this barcode", "which item", "resolve barcode",
        "barcode suchen", "strichcode suchen", "welcher artikel", "rechercher code barre", "quel produit",
        "buscar codigo de barras", "que producto", "cerca codice a barre", "quale prodotto",
        "ابحث عن الباركود", "ما هو الصنف", "لأي صنف"
    ];

    private static readonly string[] HelpWords =
    [
        "ne sorabilirim", "yardim", "ornek soru", "neler yapabilirsin", "help", "what can i ask", "what can you do",
        "hilfe", "was kann ich fragen", "aide", "que puis-je demander", "ayuda", "que puedo preguntar",
        "aiuto", "cosa posso chiedere", "مساعدة", "ماذا يمكنني ان اسال"
    ];

    private static readonly string[] AllUsersWords =
    [
        "herkes", "tum kullanici", "butun kullanici", "tum personel", "ekipteki herkes",
        "everyone", "all users", "all staff", "whole team", "alle benutzer", "alle mitarbeiter",
        "tous les utilisateurs", "toute l equipe", "todos los usuarios", "todo el personal",
        "tutti gli utenti", "tutto il personale", "جميع المستخدمين", "كل الموظفين"
    ];

    public Task<WarehouseAssistantIntentResolution> ResolveAsync(
        string message,
        WarehouseAssistantContext? context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(message);
        var datePreset = ResolveDatePreset(normalized);
        var containsSerialWord = ContainsAny(normalized, SerialWords);
        var serialNo = containsSerialWord
            ? ExtractSerial(message, normalized) ?? context?.SerialNo
            : null;
        var hasSerial = containsSerialWord || (normalized.Contains("bu seri", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(context?.SerialNo));
        var hasStock = ContainsAny(normalized, StockWords);
        var hasBalance = ContainsAny(normalized, BalanceWords);
        var hasReceipt = ContainsAny(normalized, ReceiptWords);
        var hasActivity = ContainsAny(normalized, ActivityWords);
        var hasMovement = ContainsAny(normalized, MovementWords);
        var hasTask = ContainsAny(normalized, TaskWords);
        var extractedBarcode = ExtractBarcode(message);
        var hasBarcodeLookup = ContainsAny(normalized, BarcodeLookupWords)
            || (ContainsAny(normalized, BarcodeWords)
                && ContainsAny(normalized,
                [
                    "sorgula", "hangi stok", "neye ait", "kime ait", "cozumle", "nedir",
                    "lookup", "which item", "what is", "belongs to", "resolve", "suchen", "welcher artikel",
                    "rechercher", "quel produit", "buscar", "que producto", "cerca", "quale prodotto", "ابحث", "صنف"
                ]))
            || (ContainsAny(normalized, BarcodeWords)
                && !string.IsNullOrWhiteSpace(extractedBarcode)
                && (extractedBarcode.Any(char.IsDigit) || extractedBarcode.IndexOfAny(['-', '/', '.']) >= 0));
        var barcode = hasBarcodeLookup ? extractedBarcode ?? context?.Barcode : null;
        var requestsAll = ContainsAny(normalized, AllUsersWords);

        WarehouseAssistantIntent intent;
        decimal confidence;
        if (ContainsAny(normalized, HelpWords))
        {
            intent = WarehouseAssistantIntent.Help;
            confidence = 1m;
        }
        else if (hasBarcodeLookup)
        {
            intent = WarehouseAssistantIntent.BarcodeLookup;
            confidence = string.IsNullOrWhiteSpace(barcode) ? 0.70m : 0.99m;
        }
        else if (hasTask)
        {
            intent = WarehouseAssistantIntent.AssignedTasks;
            confidence = 0.96m;
        }
        else if (hasMovement && (hasSerial || hasStock))
        {
            intent = WarehouseAssistantIntent.StockMovementHistory;
            confidence = string.IsNullOrWhiteSpace(serialNo) && !hasStock ? 0.70m : 0.95m;
        }
        else if (hasSerial && hasReceipt)
        {
            intent = WarehouseAssistantIntent.SerialReceiptHistory;
            confidence = string.IsNullOrWhiteSpace(serialNo) ? 0.70m : 0.98m;
        }
        else if (hasSerial && hasBalance)
        {
            intent = WarehouseAssistantIntent.SerialBalance;
            confidence = string.IsNullOrWhiteSpace(serialNo) ? 0.70m : 0.98m;
        }
        else if (hasStock && hasBalance)
        {
            intent = WarehouseAssistantIntent.StockLocationBalance;
            confidence = 0.90m;
        }
        else if (hasActivity)
        {
            intent = requestsAll ? WarehouseAssistantIntent.UserActivities : WarehouseAssistantIntent.MyActivities;
            confidence = 0.92m;
        }
        else
        {
            intent = WarehouseAssistantIntent.Unknown;
            confidence = 0.20m;
        }

        return Task.FromResult(new WarehouseAssistantIntentResolution(
            intent,
            datePreset,
            serialNo,
            hasStock ? message.Trim() : context?.StockCode,
            barcode,
            null,
            requestsAll,
            confidence,
            "deterministic"));
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToLower(new CultureInfo("tr-TR")).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character switch { 'ı' => 'i', _ => character });
        }
        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ");
    }

    private static WarehouseAssistantDatePreset ResolveDatePreset(string normalized)
    {
        if (ContainsAny(normalized, ["dun", "yesterday", "gestern", "hier", "ayer", "ieri", "امس"]))
            return WarehouseAssistantDatePreset.Yesterday;
        if (ContainsAny(normalized, ["bu hafta", "this week", "diese woche", "cette semaine", "esta semana", "questa settimana", "هذا الاسبوع"]))
            return WarehouseAssistantDatePreset.ThisWeek;
        if (ContainsAny(normalized, ["son 30 gun", "son otuz gun", "bu ay", "last 30 days", "this month", "letzte 30 tage", "diesen monat", "30 derniers jours", "ce mois", "ultimos 30 dias", "este mes", "ultimi 30 giorni", "questo mese", "اخر 30 يوم", "هذا الشهر"]))
            return WarehouseAssistantDatePreset.LastThirtyDays;
        if (ContainsAny(normalized, ["son 7 gun", "son yedi gun", "last 7 days", "letzte 7 tage", "7 derniers jours", "ultimos 7 dias", "ultimi 7 giorni", "اخر 7 ايام"]))
            return WarehouseAssistantDatePreset.LastSevenDays;
        return WarehouseAssistantDatePreset.Today;
    }

    private static string? ExtractSerial(string original, string normalized)
    {
        var valueBeforeSerialWord = Regex.Match(original,
            @"\b([A-Za-z0-9][A-Za-z0-9._/\-]{1,99})\s+seri(?:si|sinin|sine|de|den)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (valueBeforeSerialWord.Success && !IsSerialStopWord(Normalize(valueBeforeSerialWord.Groups[1].Value)))
            return valueBeforeSerialWord.Groups[1].Value.Trim(' ', '\'', '"');

        var explicitValue = Regex.Match(original,
            """(?:seri(?:\s*(?:no|numarası|numarasi))?|serial|barkod|etiket)\b\s*(?:[:=#]\s*)?["']?([A-Za-z0-9][A-Za-z0-9._/\-]{1,99})""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (explicitValue.Success && !IsSerialStopWord(Normalize(explicitValue.Groups[1].Value)))
            return explicitValue.Groups[1].Value.Trim(' ', '\'', '"');

        var codeLike = Regex.Matches(original, @"\b[A-Za-z0-9]+(?:[-/._][A-Za-z0-9]+)+\b", RegexOptions.CultureInvariant)
            .Select(x => x.Value)
            .FirstOrDefault(x => !Normalize(x).Contains("mal-kabul", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(codeLike)) return codeLike.Trim();

        if (normalized.Contains("bu seri", StringComparison.Ordinal)) return null;
        return null;
    }

    private static string? ExtractBarcode(string original)
    {
        var quoted = Regex.Match(original,
            """(?:barkod|etiket|barcode|label|strichcode|code\s*barre|codigo\s*de\s*barras|codice\s*a\s*barre|باركود|رمز\s*شريطي)(?:u|un|in|i)?\s*(?:no|number|nummer|numero|numarası|numarasi|değeri|degeri|value|wert|valeur|valor|قيمة)?\s*(?:[:=#]\s*)?[\"']([^\"']{2,250})[\"']""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (quoted.Success) return quoted.Groups[1].Value.Trim();

        var explicitValue = Regex.Match(original,
            """(?:barkod|etiket|barcode|label|strichcode|code\s*barre|codigo\s*de\s*barras|codice\s*a\s*barre|باركود|رمز\s*شريطي)(?:u|un|in|i)?\s*(?:no|number|nummer|numero|numarası|numarasi|değeri|degeri|value|wert|valeur|valor|قيمة)?\s*(?:[:=#]\s*)?([^\s?,;]{2,250})""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!explicitValue.Success) return null;
        var value = explicitValue.Groups[1].Value.Trim(' ', '\'', '"', '.', ':');
        return IsSerialStopWord(Normalize(value)) ? null : value;
    }

    private static bool IsSerialStopWord(string value) =>
        new[] { "bu", "bakiye", "nerede", "miktar", "ne", "kim", "hangi", "kac", "this", "which", "what", "where", "balance", "quantity" }
            .Any(value.StartsWith);

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
}
