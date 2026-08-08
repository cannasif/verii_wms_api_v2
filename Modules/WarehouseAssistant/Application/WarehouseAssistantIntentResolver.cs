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

    private static readonly string[] SupplierWords =
    [
        "cari", "tedarikci", "firma", "satici", "supplier", "vendor", "customer",
        "lieferant", "fournisseur", "proveedor", "fornitore", "مورد"
    ];

    private static readonly string[] ReceiptAnalysisWords =
    [
        "kac mal kabul", "kaç mal kabul", "neler alindi", "neler alındı", "hangi urunler alindi", "hangi ürünler alındı",
        "mal kabul raporu", "mal kabulleri", "goods receipts", "what was received", "received items",
        "wareneingange", "articles recus", "mercancia recibida", "articoli ricevuti", "إيصالات البضائع"
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
        "atanan emir", "atanmis emir", "atanan gorev", "atanmis gorev", "bana atanan", "bana atanmis",
        "gorevlerim", "gorev list", "bekleyen gorev", "acik gorev", "toplama emri", "is emirlerim", "is emri list",
        "assigned task", "my tasks", "open tasks", "pending tasks", "picking task", "work orders",
        "zugewiesene aufgabe", "meine aufgaben", "offene aufgaben", "meine offenen aufgaben", "kommissionierauftrag",
        "tache assignee", "mes taches", "taches ouvertes", "ordre de preparation",
        "tarea asignada", "mis tareas", "tareas abiertas", "orden de preparacion",
        "attivita assegnata", "i miei compiti", "attivita aperte", "ordine di prelievo",
        "المهام المسندة", "مهامي", "المهام المفتوحة", "امر تجميع"
    ];

    private static readonly string[] SteelVehicleWords =
    [
        "sac arac", "sac kabul arac", "sac mal kabul", "levha arac", "levha kabul", "arac giris", "arac kabul",
        "steel vehicle", "steel receipt vehicle", "sheet vehicle", "vehicle check in", "vehicle acceptance",
        "stahl fahrzeug", "blech annahme", "vehicule acier", "reception tole", "vehiculo acero", "recepcion chapa",
        "veicolo acciaio", "ricevimento lamiera"
    ];

    private static readonly string[] TransferWords =
    [
        "transfer", "depolar arasi", "depo transfer", "uretime transfer", "uretim transfer",
        "warehouse transfer", "inter warehouse", "production transfer", "material supply",
        "lagertransfer", "produktionstransfer", "transfert entrepot", "transfert production",
        "transferencia almacen", "transferencia produccion", "trasferimento magazzino", "trasferimento produzione"
    ];

    private static readonly string[] ProductionTransferWords =
    [
        "uretime transfer", "uretim transfer", "uretime malzeme", "uretim malzeme", "uretim besleme",
        "production transfer", "production material", "material supply", "produktionstransfer",
        "transfert production", "transferencia produccion", "trasferimento produzione"
    ];

    private static readonly string[] InterWarehouseTransferWords =
    [
        "normal transfer", "depolar arasi transfer", "depo transfer", "inter warehouse", "warehouse transfer",
        "lagertransfer", "transfert entrepot", "transferencia almacen", "trasferimento magazzino"
    ];

    private static readonly string[] TransferAnalysisWords =
    [
        "kac", "listele", "goster", "durum", "tamamlanan", "tamamlanmayan", "bekleyen", "eksik", "toplam",
        "ne kadar", "hangi", "what", "how many", "show", "list", "status", "completed", "pending", "shortage"
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
        var (datePreset, hasExplicitDatePreset) = ResolveDatePreset(normalized);
        var (dateFrom, dateTo) = ExtractExplicitDateRange(message);
        var hasExplicitDateFilter = hasExplicitDatePreset || dateFrom.HasValue;
        var containsSerialWord = ContainsAny(normalized, SerialWords);
        var serialNo = containsSerialWord
            ? ExtractSerial(message, normalized) ?? context?.SerialNo
            : null;
        var hasSerial = containsSerialWord || (normalized.Contains("bu seri", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(context?.SerialNo));
        var hasStock = ContainsAny(normalized, StockWords);
        var hasBalance = ContainsAny(normalized, BalanceWords);
        var hasReceipt = ContainsAny(normalized, ReceiptWords);
        var hasSupplier = ContainsAny(normalized, SupplierWords);
        var hasReceiptAnalysis = hasReceipt
            && (hasSupplier || dateFrom.HasValue || ContainsAny(normalized, ReceiptAnalysisWords));
        var hasActivity = ContainsAny(normalized, ActivityWords);
        var hasMovement = ContainsAny(normalized, MovementWords);
        var hasTask = ContainsAny(normalized, TaskWords)
            || ((normalized.Contains("atan", StringComparison.Ordinal)
                    || normalized.Contains("bana", StringComparison.Ordinal))
                && ContainsAny(normalized, ["gorev", "emir", "is emri"]));
        var hasSteelVehicle = ContainsAny(normalized, SteelVehicleWords)
            || (ContainsAny(normalized, ["sac", "levha", "steel", "sheet"])
                && ContainsAny(normalized, ["arac", "plaka", "giris", "kabul", "vehicle", "plate", "check in"]));
        var hasTransfer = ContainsAny(normalized, TransferWords);
        var hasTransferAnalysis = hasTransfer
            && (ContainsAny(normalized, TransferAnalysisWords)
                || !string.IsNullOrWhiteSpace(ExtractTransferDocument(message)));
        var transferScope = ContainsAny(normalized, ProductionTransferWords)
            ? WarehouseAssistantTransferScope.Production
            : ContainsAny(normalized, InterWarehouseTransferWords)
                ? WarehouseAssistantTransferScope.InterWarehouse
                : WarehouseAssistantTransferScope.All;
        var vehiclePlate = hasSteelVehicle ? ExtractVehiclePlate(message) : null;
        var transferDocument = hasTransfer ? ExtractTransferDocument(message) : null;
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
        else if (hasSteelVehicle)
        {
            intent = WarehouseAssistantIntent.SteelVehicleAnalysis;
            confidence = string.IsNullOrWhiteSpace(vehiclePlate) ? 0.95m : 0.99m;
        }
        else if (hasTransferAnalysis)
        {
            intent = WarehouseAssistantIntent.WarehouseTransferAnalysis;
            confidence = string.IsNullOrWhiteSpace(transferDocument) ? 0.95m : 0.99m;
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
        else if (hasReceiptAnalysis)
        {
            intent = WarehouseAssistantIntent.GoodsReceiptAnalysis;
            confidence = hasSupplier && dateFrom.HasValue ? 0.99m : 0.93m;
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
            "deterministic",
            dateFrom,
            dateTo,
            hasSupplier ? message.Trim() : context?.SupplierCode,
            VehiclePlateQuery: vehiclePlate ?? context?.VehiclePlate,
            TransferDocumentQuery: transferDocument ?? context?.TransferDocumentNo,
            TransferScope: hasTransfer ? transferScope : context?.TransferScope ?? WarehouseAssistantTransferScope.All,
            HasExplicitDateFilter: hasExplicitDateFilter));
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

    private static (WarehouseAssistantDatePreset Preset, bool IsExplicit) ResolveDatePreset(string normalized)
    {
        if (ContainsAny(normalized, ["dun", "yesterday", "gestern", "hier", "ayer", "ieri", "امس"]))
            return (WarehouseAssistantDatePreset.Yesterday, true);
        if (ContainsAny(normalized, ["bu hafta", "this week", "diese woche", "cette semaine", "esta semana", "questa settimana", "هذا الاسبوع"]))
            return (WarehouseAssistantDatePreset.ThisWeek, true);
        if (ContainsAny(normalized, ["son 30 gun", "son otuz gun", "bu ay", "last 30 days", "this month", "letzte 30 tage", "diesen monat", "30 derniers jours", "ce mois", "ultimos 30 dias", "este mes", "ultimi 30 giorni", "questo mese", "اخر 30 يوم", "هذا الشهر"]))
            return (WarehouseAssistantDatePreset.LastThirtyDays, true);
        if (ContainsAny(normalized, ["son 7 gun", "son yedi gun", "last 7 days", "letzte 7 tage", "7 derniers jours", "ultimos 7 dias", "ultimi 7 giorni", "اخر 7 ايام"]))
            return (WarehouseAssistantDatePreset.LastSevenDays, true);
        if (ContainsAny(normalized, ["bugun", "today", "heute", "aujourd hui", "hoy", "oggi", "اليوم"]))
            return (WarehouseAssistantDatePreset.Today, true);
        return (WarehouseAssistantDatePreset.Today, false);
    }

    private static (DateOnly? From, DateOnly? To) ExtractExplicitDateRange(string message)
    {
        var dates = new List<DateOnly>(2);
        foreach (Match match in Regex.Matches(message, @"(?<!\d)(\d{1,4})[./-](\d{1,2})[./-](\d{1,4})(?!\d)", RegexOptions.CultureInvariant))
        {
            var raw = match.Value;
            var formats = match.Groups[1].Value.Length == 4
                ? new[] { "yyyy-M-d", "yyyy-MM-dd", "yyyy.M.d", "yyyy.MM.dd", "yyyy/M/d", "yyyy/MM/dd" }
                : new[] { "d.M.yyyy", "dd.MM.yyyy", "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy" };
            if (DateOnly.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                dates.Add(parsed);
            if (dates.Count == 2) break;
        }

        if (dates.Count == 0) return (null, null);
        if (dates.Count == 1) return (dates[0], dates[0]);
        return dates[0] <= dates[1] ? (dates[0], dates[1]) : (dates[1], dates[0]);
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

    private static string? ExtractVehiclePlate(string original)
    {
        var explicitPlate = Regex.Match(original,
            @"(?:plaka|plate|kennzeichen|matricule|matricula|targa)\s*(?:no|numarasi|number)?\s*(?:[:=#]\s*)?([A-Za-z0-9\s-]{4,20})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (explicitPlate.Success)
        {
            var value = Regex.Match(explicitPlate.Groups[1].Value, @"\d{2}\s*[A-Za-z]{1,3}\s*\d{2,5}", RegexOptions.IgnoreCase).Value;
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        var turkishPlate = Regex.Match(original, @"(?<!\d)\d{2}\s*[A-Za-z]{1,3}\s*\d{2,5}(?!\d)", RegexOptions.IgnoreCase);
        return turkishPlate.Success ? turkishPlate.Value.Trim() : null;
    }

    private static string? ExtractTransferDocument(string original)
    {
        var explicitDocument = Regex.Match(original,
            @"(?:transfer|emir|belge)\s*(?:no|numarasi|number)\s*(?:[:=#]\s*)?([A-Za-z][A-Za-z0-9._/-]{2,60})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (explicitDocument.Success)
        {
            var value = explicitDocument.Groups[1].Value.Trim(' ', '.', ',', ';', ':');
            if (!ContainsAny(Normalize(value), ["durum", "liste", "kac", "goster", "emirler", "transferler"]))
                return value;
        }

        var prefixed = Regex.Match(
            original,
            @"\b(?:WT|PT|DAT|TR|MK)(?=[-_]?[A-Za-z0-9._/-]*\d)[-_]?[A-Za-z0-9._/-]{3,60}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return prefixed.Success ? prefixed.Value.Trim() : null;
    }

    private static bool IsSerialStopWord(string value) =>
        new[] { "bu", "bakiye", "nerede", "miktar", "ne", "kim", "hangi", "kac", "this", "which", "what", "where", "balance", "quantity" }
            .Any(value.StartsWith);

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
}
