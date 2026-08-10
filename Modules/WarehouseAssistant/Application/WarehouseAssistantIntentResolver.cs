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
        "islem", "hareket", "yaptigim", "yapmis", "yapti", "aktivit", "kayit", "neyle ugrastim", "neyle ugrasmis", "ne yapmis", "neler yapmis", "ne is yapmis",
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
        "gorevlerim", "gorev list", "bekleyen gorev", "acik gorev", "toplama emri", "is emirlerim", "is emri list", "benden beklenen", "siradaki islerim", "yapmam gereken emir",
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

    private static readonly string[] ShiftBriefWords =
    [
        "vardiya ozeti", "gunluk depo ozeti", "bugunku islerimi ozetle", "bugun ne yapacagim", "bugun ne yapmaliyim",
        "mesaiye basladim", "once neye bakayim", "once ne yapayim", "onceligim ne", "nereden baslamaliyim", "bugun beni ne bekliyor", "ilk hangi isi yapayim",
        "deponun bugunku durumu", "operasyon ozeti", "shift brief", "shift summary", "today s workload", "warehouse summary",
        "schichtubersicht", "resume de l equipe", "resumen del turno", "riepilogo turno"
    ];

    private static readonly string[] OperationalExceptionWords =
    [
        "mudahale gereken", "mudahele gereken", "operasyon sorunlari", "kritik sorun", "istisna merkezi",
        "erp ye gitmeyen", "erp aktarimi basarisiz", "basarisiz job", "hangfire hatasi", "bakiye tutarsiz",
        "kalitede bekleyen", "geciken sevk", "takilan islemler", "ters giden", "aksayan is", "sorunlu islemler", "yolunda gitmeyen", "acil bakmam gereken",
        "exception center", "operational exceptions",
        "failed erp", "failed jobs", "stuck operations", "warehouse issues"
    ];

    private static readonly string[] TraceabilityWords =
    [
        "izlenebilirlik", "urun hikayesi", "serinin hikayesi", "barkodun hikayesi", "uctan uca", "nereden geldi nereye gitti",
        "hangi islemlerden gecti", "hangi yollardan gecti", "basina neler geldi", "tum yolculugu", "ilk giristen simdiye", "gecmisini goster", "traceability", "trace history", "end to end history", "where did it come from",
        "ruckverfolgbarkeit", "tracabilite", "trazabilidad", "tracciabilita"
    ];

    private static readonly string[] ProcessBlockerWords =
    [
        "neden bekliyor", "neden tamamlanamiyor", "neden ilerlemiyor", "neden takildi", "ne engelliyor", "engel nedir",
        "hangi adimda kaldi", "onunde ne var", "neye takildi", "niye bitmiyor", "devam etmesini ne durduruyor", "neden erp ye gitmiyor", "why is it waiting", "why is it blocked", "why can t it complete", "blocking reason",
        "warum blockiert", "pourquoi bloque", "por que bloqueado", "perche bloccato"
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
        var usesPendingQuestion = ShouldUsePendingQuestion(message, context);
        var analysisMessage = usesPendingQuestion
            ? $"{context!.PendingQuestion} {message}"
            : message;
        var normalized = Normalize(analysisMessage);
        var language = new LocalWarehouseQuestion(normalized);
        var isFollowUp = usesPendingQuestion || IsConversationFollowUp(normalized);
        var (datePreset, hasExplicitDatePreset) = ResolveDatePreset(normalized);
        var (dateFrom, dateTo) = ExtractExplicitDateRange(analysisMessage);
        var hasExplicitDateFilter = hasExplicitDatePreset || dateFrom.HasValue;
        var containsSerialWord = language.HasAny(SerialWords);
        var serialNo = containsSerialWord
            ? ExtractSerial(analysisMessage, normalized) ?? context?.SerialNo
            : null;
        var hasSerial = containsSerialWord || (normalized.Contains("bu seri", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(context?.SerialNo));
        var hasStock = language.HasAny(StockWords);
        var hasBalance = language.HasAny(BalanceWords);
        var hasReceipt = language.HasAny(ReceiptWords);
        var hasSupplier = language.HasAny(SupplierWords);
        var hasReceiptAnalysis = hasReceipt
            && (hasSupplier || dateFrom.HasValue || language.HasAny(ReceiptAnalysisWords));
        var hasActivity = language.HasAny(ActivityWords);
        var hasMovement = language.HasAny(MovementWords);
        var hasTask = language.HasAny(TaskWords)
            || ((normalized.Contains("atan", StringComparison.Ordinal)
                    || normalized.Contains("bana", StringComparison.Ordinal))
                && language.HasAny("gorev", "emir", "is emri"));
        var vehiclePlate = ExtractVehiclePlate(analysisMessage);
        var hasSteelVehicle = language.HasAny(SteelVehicleWords)
            || (language.HasAny("sac", "levha", "steel", "sheet")
                && language.HasAny("arac", "plaka", "giris", "kabul", "vehicle", "plate", "check in"))
            || (!string.IsNullOrWhiteSpace(vehiclePlate)
                && language.HasAny("geldi", "girdi", "arac", "tir", "kamyon", "kabul", "arrived", "entered"));
        var hasTransfer = language.HasAny(TransferWords)
            || (language.HasAny("uretime", "uretim")
                && language.HasAny("giden", "verilen", "malzeme", "besleme", "eksik", "kalan"));
        var hasTransferAnalysis = hasTransfer
            && (language.HasAny(TransferAnalysisWords)
                || !string.IsNullOrWhiteSpace(ExtractTransferDocument(message)));
        var transferScope = language.HasAny(ProductionTransferWords)
            || (language.HasAny("uretime", "uretim") && language.HasAny("giden", "verilen", "malzeme", "besleme"))
            ? WarehouseAssistantTransferScope.Production
            : language.HasAny(InterWarehouseTransferWords)
                ? WarehouseAssistantTransferScope.InterWarehouse
                : WarehouseAssistantTransferScope.All;
        var transferDocument = hasTransfer ? ExtractTransferDocument(analysisMessage) : null;
        var extractedBarcode = ExtractBarcode(analysisMessage);
        var hasBarcodeLookup = language.HasAny(BarcodeLookupWords)
            || (language.HasAny(BarcodeWords)
                && language.HasAny(
                [
                    "sorgula", "hangi stok", "neye ait", "kime ait", "cozumle", "nedir",
                    "lookup", "which item", "what is", "belongs to", "resolve", "suchen", "welcher artikel",
                    "rechercher", "quel produit", "buscar", "que producto", "cerca", "quale prodotto", "ابحث", "صنف"
                ]))
            || (language.HasAny(BarcodeWords)
                && !string.IsNullOrWhiteSpace(extractedBarcode)
                && (extractedBarcode.Any(char.IsDigit) || extractedBarcode.IndexOfAny(['-', '/', '.']) >= 0));
        var barcode = hasBarcodeLookup ? extractedBarcode ?? context?.Barcode : null;
        var requestsAll = language.HasAny(AllUsersWords);
        var hasShiftBrief = language.HasAny(ShiftBriefWords);
        var hasOperationalExceptions = language.HasAny(OperationalExceptionWords);
        var hasTraceability = language.HasAny(TraceabilityWords);
        var hasProcessBlocker = language.HasAny(ProcessBlockerWords);
        var documentQuery = ExtractProcessDocument(analysisMessage) ?? context?.DocumentNo;

        WarehouseAssistantIntent intent;
        decimal confidence;
        if (language.HasAny(HelpWords))
        {
            intent = WarehouseAssistantIntent.Help;
            confidence = 1m;
        }
        else if (hasShiftBrief)
        {
            intent = WarehouseAssistantIntent.ShiftBrief;
            confidence = 0.99m;
        }
        else if (hasOperationalExceptions)
        {
            intent = WarehouseAssistantIntent.OperationalExceptions;
            confidence = 0.98m;
        }
        else if (hasProcessBlocker)
        {
            intent = WarehouseAssistantIntent.ProcessBlockers;
            confidence = string.IsNullOrWhiteSpace(documentQuery) ? 0.72m : 0.99m;
        }
        else if (hasTraceability && !hasSteelVehicle && !hasTransferAnalysis)
        {
            intent = WarehouseAssistantIntent.Traceability;
            confidence = string.IsNullOrWhiteSpace(serialNo) && string.IsNullOrWhiteSpace(extractedBarcode) ? 0.75m : 0.99m;
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
        else if (context?.LastIntent is { } lastIntent
            && lastIntent is not WarehouseAssistantIntent.Unknown and not WarehouseAssistantIntent.Help
            && (hasExplicitDateFilter || IsConversationFollowUp(normalized)))
        {
            intent = lastIntent;
            confidence = 0.82m;
        }
        else
        {
            intent = WarehouseAssistantIntent.Unknown;
            confidence = 0.20m;
        }

        var localDecision = LocalWarehouseLanguageEngine.Resolve(normalized, requestsAll);
        if (localDecision.IsWriteRequest)
        {
            intent = WarehouseAssistantIntent.Unknown;
            confidence = 1m;
        }
        else if (!localDecision.IsAmbiguous
            && localDecision.Intent != WarehouseAssistantIntent.Unknown
            && (intent == WarehouseAssistantIntent.Unknown
                || localDecision.Intent == intent))
        {
            intent = localDecision.Intent;
            confidence = Math.Max(confidence, localDecision.Confidence);
        }

        if (intent is WarehouseAssistantIntent.Traceability
            or WarehouseAssistantIntent.SerialBalance
            or WarehouseAssistantIntent.SerialReceiptHistory
            or WarehouseAssistantIntent.StockMovementHistory)
        {
            serialNo ??= ExtractSerial(analysisMessage, normalized) ?? context?.SerialNo;
        }

        var stockQuery = intent is WarehouseAssistantIntent.StockLocationBalance or WarehouseAssistantIntent.StockMovementHistory
            ? analysisMessage.Trim()
            : hasStock ? analysisMessage.Trim() : context?.StockCode;

        return Task.FromResult(new WarehouseAssistantIntentResolution(
            intent,
            datePreset,
            serialNo,
            stockQuery,
            barcode,
            isFollowUp ? context?.TargetUserQuery : null,
            requestsAll || (isFollowUp && context?.RequestsAllUsers == true),
            confidence,
            LocalWarehouseLanguageEngine.ProviderMode,
            dateFrom,
            dateTo,
            hasSupplier ? analysisMessage.Trim() : context?.SupplierCode,
            VehiclePlateQuery: vehiclePlate ?? context?.VehiclePlate,
            TransferDocumentQuery: transferDocument ?? context?.TransferDocumentNo,
            TransferScope: hasTransfer ? transferScope : context?.TransferScope ?? WarehouseAssistantTransferScope.All,
            HasExplicitDateFilter: hasExplicitDateFilter,
            DocumentQuery: documentQuery));
    }

    private static bool ShouldUsePendingQuestion(string message, WarehouseAssistantContext? context) =>
        !string.IsNullOrWhiteSpace(context?.PendingQuestion)
        && message.Trim().Length <= 160;

    private static bool IsConversationFollowUp(string normalized) => ContainsAny(normalized,
    [
        "peki", "ya onceki", "ya gecen", "bir de", "ayni sorgu", "aynisini", "bunlar", "onlar",
        "what about", "and previous", "same query", "wie sieht", "et pour", "y que", "e invece"
    ]);

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
        if (ContainsAny(normalized, ["gecen hafta", "last week", "letzte woche", "semaine derniere", "semana pasada", "settimana scorsa", "الأسبوع الماضي"]))
            return (WarehouseAssistantDatePreset.LastWeek, true);
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

    private static string? ExtractProcessDocument(string original)
    {
        var quoted = Regex.Match(original,
            """(?:belge|dokuman|irsaliye|mal\s*kabul|transfer|sevk|paket|emir)\s*(?:no|numarasi|number)?\s*(?:[:=#]\s*)?["']([A-Za-z0-9][A-Za-z0-9._/\-]{2,99})["']""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (quoted.Success) return quoted.Groups[1].Value.Trim();

        var explicitDocument = Regex.Match(original,
            @"(?:belge|dokuman|irsaliye|mal\s*kabul|transfer|sevk|paket|emir)\s*(?:no|numarasi|number)?\s*(?:[:=#]\s*)?([A-Za-z][A-Za-z0-9._/\-]{2,99})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (explicitDocument.Success)
        {
            var value = explicitDocument.Groups[1].Value.Trim(' ', '.', ',', ';', ':');
            if (!ContainsAny(Normalize(value), ["neden", "bekliyor", "tamamlanamiyor", "ilerlemiyor", "durum", "goster"]))
                return value;
        }

        var prefixed = Regex.Match(original,
            @"\b(?:GRI|GR|WT|PT|DAT|SHP|WI|WO|PKG|QC|KKD)[-_][A-Za-z0-9._/\-]{3,99}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return prefixed.Success ? prefixed.Value.Trim() : null;
    }

    private static bool IsSerialStopWord(string value) =>
        new[] { "bu", "bakiye", "nerede", "miktar", "ne", "kim", "hangi", "kac", "this", "which", "what", "where", "balance", "quantity" }
            .Any(value.StartsWith);

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        new LocalWarehouseQuestion(value).HasAny(candidates);
}
