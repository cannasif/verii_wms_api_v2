using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

internal sealed record LocalWarehouseIntentDecision(
    WarehouseAssistantIntent Intent,
    decimal Confidence,
    int Score,
    int Margin,
    bool IsAmbiguous,
    bool IsWriteRequest);

/// <summary>
/// Offline WMS language layer. It intentionally performs intent planning only;
/// authorization, entity resolution and data access remain in the application service.
/// </summary>
internal static class LocalWarehouseLanguageEngine
{
    public const string ProviderMode = "local-inprocess-v2.5";

    public static LocalWarehouseIntentDecision Resolve(string normalizedMessage, bool requestsAllUsers)
    {
        var question = new LocalWarehouseQuestion(normalizedMessage);
        if (question.IsWriteRequest())
            return new LocalWarehouseIntentDecision(WarehouseAssistantIntent.Unknown, 1m, 100, 100, false, true);

        var scores = new Dictionary<WarehouseAssistantIntent, int>();
        void Add(WarehouseAssistantIntent intent, int score)
        {
            if (score <= 0) return;
            scores[intent] = scores.TryGetValue(intent, out var current) ? current + score : score;
        }

        var hasCode = Regex.IsMatch(normalizedMessage, @"\b[a-z0-9]+(?:[-/._][a-z0-9]+)+\b", RegexOptions.CultureInvariant);
        var hasPlate = Regex.IsMatch(normalizedMessage, @"(?<!\d)\d{2}\s*[a-z]{1,3}\s*\d{2,5}(?!\d)", RegexOptions.CultureInvariant);
        var hasDate = question.HasAny("bugun", "dun", "hafta", "ay", "tarih", "gun", "today", "yesterday", "week", "month");
        var hasWhere = question.HasAny(
            "nerede", "nereye konmus", "hangi raf", "hangi depo", "konum", "lokasyon",
            "nerelere dagilmis", "nerede duruyor", "where", "location", "bin");
        var hasQuantity = question.HasAny(
            "bakiye", "miktar", "kac", "ne kadar", "adet", "elde", "elimizde", "mevcut",
            "kalmis", "kaldi", "var mi", "balance", "quantity", "how many");
        var hasSerial = question.HasAny("seri", "serino", "seri no", "serial", "barkodlu seri");
        var hasStock = question.HasAny("stok", "urun", "malzeme", "mamul", "parca", "item", "product", "material");
        var hasReceipt = question.HasAny(
            "mal kabul", "irsaliye", "kabul", "iceri al", "iceri girmis", "gelen malzeme",
            "depoya ulasan", "depoya gelen", "ne gelmis", "neler gelmis", "ne almisiz",
            "neler almisiz", "tesellum", "goods receipt", "received", "inbound");
        var hasSupplier = question.HasAny("cari", "tedarikci", "satici", "firma", "supplier", "vendor");
        var hasTransfer = question.HasAny("transfer", "depolar arasi", "uretime giden", "uretime verilen", "uretime gonderilen", "uretim besleme", "production supply");

        if (question.HasAny("yardim", "ne sorabilirim", "neler yapabilirsin", "ornek soru", "help", "what can i ask"))
            Add(WarehouseAssistantIntent.Help, 12);

        var hasSetting = question.HasAny("parametre", "ayar", "secenek", "politika", "kural");
        var asksSettingEffect = question.HasAny(
            "ne ise yarar", "ne olur", "ne degisiyor", "neyi degistirir", "nereleri etkiler", "acarsam", "kapatirsam",
            "hangisini secmeliyim", "farki ne", "ornek senaryo", "what does", "what happens");
        if (hasSetting && asksSettingEffect)
            Add(WarehouseAssistantIntent.ParameterHelp, 11);

        if (question.HasAny("vardiya ozeti", "gunluk ozet", "mesaiye basladim", "bugun beni ne bekliyor", "nereden baslamaliyim", "once neye bakayim", "onceligim ne", "shift brief"))
            Add(WarehouseAssistantIntent.ShiftBrief, 11);
        if (question.HasAny("ozet", "oncelik", "is yuk", "yapacaklarim") && hasDate)
            Add(WarehouseAssistantIntent.ShiftBrief, 6);

        var hasProblem = question.HasAny("sorun", "hata", "aksayan", "ters giden", "basarisiz", "tutarsiz", "kritik", "mudahale", "takilan", "geciken");
        if (hasProblem) Add(WarehouseAssistantIntent.OperationalExceptions, 5);
        if (hasProblem && question.HasAny("operasyon", "erp", "job", "hangfire", "bakiye", "sevk", "kalite", "depo"))
            Add(WarehouseAssistantIntent.OperationalExceptions, 4);

        var hasWhy = question.HasAny("neden", "niye", "ne engelliyor", "hangi adimda", "neye takildi", "why", "blocking reason");
        var hasBlocked = question.HasAny("bekliyor", "ilerlemiyor", "tamamlanamiyor", "bitmiyor", "takildi", "blok", "engelli", "erp ye gitmiyor");
        if (hasWhy && hasBlocked) Add(WarehouseAssistantIntent.ProcessBlockers, 10);
        else if ((hasWhy || hasBlocked) && hasCode) Add(WarehouseAssistantIntent.ProcessBlockers, 7);

        var hasJourney = question.HasAny(
            "izlenebilirlik", "hikaye", "yolculuk", "bastan sona", "basindan beri", "uctan uca",
            "ilk giristen", "basina ne geldi", "basina neler geldi", "hangi adimlardan gecti",
            "hangi adimlardan gecmis", "hangi islemlerden gecti", "hangi islemlerden gecmis",
            "nereden geldi nereye gitti", "nerelere gitti", "traceability", "end to end");
        if (hasJourney) Add(WarehouseAssistantIntent.Traceability, 7);
        if (hasJourney && (hasSerial || hasCode || question.HasAny("barkod", "etiket", "lot")))
            Add(WarehouseAssistantIntent.Traceability, 4);

        var hasBarcode = question.HasAny("barkod", "etiket", "barcode", "label");
        var hasIdentify = question.HasAny("sorgula", "cozumle", "neye ait", "neyi gosteriyor", "hangi stok", "hangi urun", "nedir", "okut", "lookup", "identify", "resolve");
        if (hasBarcode && hasIdentify) Add(WarehouseAssistantIntent.BarcodeLookup, 10);
        else if (hasBarcode && hasCode) Add(WarehouseAssistantIntent.BarcodeLookup, 6);

        var hasTask = question.HasAny("gorev", "emir", "is emri", "toplama", "toplama isi", "yapacak is", "task", "work order");
        var hasAssignment = question.HasAny("atanan", "atanmis", "bana", "benden beklenen", "sorumlu oldugum", "yapmam gereken", "islerim", "assigned", "my task");
        if (hasTask) Add(WarehouseAssistantIntent.AssignedTasks, 4);
        if (hasTask && hasAssignment) Add(WarehouseAssistantIntent.AssignedTasks, 6);
        if (hasAssignment && question.HasAny("is", "bekleyen", "acik")) Add(WarehouseAssistantIntent.AssignedTasks, 7);

        var hasVehicle = question.HasAny("arac", "tir", "kamyon", "plaka", "vehicle", "truck", "plate");
        var hasSteel = question.HasAny("sac", "levha", "rulo", "steel", "sheet", "panel");
        var hasArrival = question.HasAny("girdi", "geldi", "arac giris", "kabul edildi", "check in", "arrived", "entered");
        if (hasSteel && (hasVehicle || hasArrival)) Add(WarehouseAssistantIntent.SteelVehicleAnalysis, 9);
        if (hasSteel && question.HasAny("giris", "kabul", "gecmis", "geldi mi"))
            Add(WarehouseAssistantIntent.SteelVehicleAnalysis, 9);
        if (hasPlate && (hasArrival || hasReceipt || hasVehicle)) Add(WarehouseAssistantIntent.SteelVehicleAnalysis, 9);

        if (hasTransfer) Add(WarehouseAssistantIntent.WarehouseTransferAnalysis, 5);
        if (hasTransfer && question.HasAny("durum", "bekleyen", "eksik", "yarim", "kalan", "tamamlanan", "kac", "liste", "goster", "ne oldu", "hangi"))
            Add(WarehouseAssistantIntent.WarehouseTransferAnalysis, 5);
        if (question.HasAny("uretime giden", "uretime verilen", "uretim besleme") && question.HasAny("eksik", "kalan", "bekleyen", "durum"))
            Add(WarehouseAssistantIntent.WarehouseTransferAnalysis, 9);

        var hasMovement = question.HasAny("hareket", "giris cikis", "nereden nereye", "yer degistir", "movement", "movement history");
        if (hasMovement && (hasStock || hasSerial || hasCode)) Add(WarehouseAssistantIntent.StockMovementHistory, 9);

        var hasWhoWhen = question.HasAny("ne zaman", "hangi tarihte", "kim aldi", "kim tarafindan", "kim kabul", "when", "who received");
        if (hasSerial && hasReceipt && hasWhoWhen) Add(WarehouseAssistantIntent.SerialReceiptHistory, 11);
        else if (hasSerial && hasReceipt) Add(WarehouseAssistantIntent.SerialReceiptHistory, 6);

        var hasReceiptAnalysis = question.HasAny(
            "kac mal kabul", "neler alindi", "ne alindi", "neler geldi", "neler gelmis", "ne gelmis", "gelen urun",
            "ne girmis", "ne almisiz", "neler almisiz", "hangi urunler geldi", "gelenleri goster",
            "mal kabul raporu", "mal kabulleri", "kalite kontrol bekleyen mal kabul", "what was received");
        if (hasReceiptAnalysis) Add(WarehouseAssistantIntent.GoodsReceiptAnalysis, 9);
        if (hasReceipt && (hasSupplier || hasDate) && question.HasAny("kac", "neler", "hangi", "liste", "goster", "rapor"))
            Add(WarehouseAssistantIntent.GoodsReceiptAnalysis, 8);
        if (hasSupplier && hasDate && question.HasAny("gelen", "gelmis", "alindi", "kabul"))
            Add(WarehouseAssistantIntent.GoodsReceiptAnalysis, 8);

        if (hasSerial && (hasWhere || hasQuantity)) Add(WarehouseAssistantIntent.SerialBalance, 10);
        if (hasStock && (hasWhere || hasQuantity)) Add(WarehouseAssistantIntent.StockLocationBalance, 9);
        if (!hasSerial && hasCode && question.HasAny("depoda", "rafta", "raflarda", "nerelere dagilmis", "stokta") && (hasWhere || hasQuantity))
            Add(WarehouseAssistantIntent.StockLocationBalance, 8);
        if (!hasSerial && hasCode && !hasReceipt && !hasTransfer && !hasPlate && (hasWhere || hasQuantity))
            Add(WarehouseAssistantIntent.StockLocationBalance, 8);

        var hasActivity = question.HasAny("yaptigim", "yapmis", "yapti", "neyle ugrastim", "ne is yapmis", "islemlerim", "aktiviteler", "calismalar", "activities", "actions", "did today");
        if (hasActivity) Add(requestsAllUsers ? WarehouseAssistantIntent.UserActivities : WarehouseAssistantIntent.MyActivities, 8);
        if (hasActivity && hasDate) Add(requestsAllUsers ? WarehouseAssistantIntent.UserActivities : WarehouseAssistantIntent.MyActivities, 2);

        if (scores.Count == 0)
            return new LocalWarehouseIntentDecision(WarehouseAssistantIntent.Unknown, 0.20m, 0, 0, false, false);

        var ordered = scores.OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToArray();
        var top = ordered[0];
        var secondScore = ordered.Length > 1 ? ordered[1].Value : 0;
        var margin = top.Value - secondScore;
        var ambiguous = top.Value < 5 || (secondScore >= 6 && margin <= 1);
        var confidence = top.Value switch
        {
            >= 11 => 0.99m,
            >= 9 => 0.96m,
            >= 7 => 0.90m,
            >= 5 => 0.80m,
            _ => 0.35m
        };

        return new LocalWarehouseIntentDecision(
            ambiguous ? WarehouseAssistantIntent.Unknown : top.Key,
            ambiguous ? Math.Min(confidence, 0.55m) : confidence,
            top.Value,
            margin,
            ambiguous,
            false);
    }
}

internal sealed class LocalWarehouseQuestion
{
    private static readonly string[] OrderedSuffixes = WarehouseAssistantTerminology.TurkishSuffixes
        .Select(WarehouseAssistantTextNormalizer.Normalize)
        .Distinct(StringComparer.Ordinal)
        .OrderByDescending(x => x.Length)
        .ToArray();

    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string normalized;
    private readonly string[] tokens;

    public LocalWarehouseQuestion(string normalizedMessage)
    {
        normalized = normalizedMessage;
        tokens = TokenRegex.Matches(normalizedMessage).Select(x => x.Value).ToArray();
    }

    public bool HasAny(params string[] candidates) => HasAny((IEnumerable<string>)candidates);

    public bool HasAny(IEnumerable<string> candidates)
    {
        foreach (var rawCandidate in candidates)
        {
            var candidate = WarehouseAssistantTextNormalizer.Normalize(rawCandidate);
            if (candidate.Length == 0) continue;
            if (ContainsPhrase(candidate)) return true;
        }
        return false;
    }

    public bool IsWriteRequest()
    {
        return WarehouseAssistantTerminology.WriteCommands.Any(ContainsImperativePhrase);
    }

    private bool ContainsPhrase(string candidate)
    {
        if (candidate.Contains(' '))
        {
            if (normalized.Contains(candidate, StringComparison.Ordinal)) return true;
            var candidateTokens = TokenRegex.Matches(candidate).Select(x => x.Value).ToArray();
            return candidateTokens.Length > 0 && candidateTokens.All(ContainsToken);
        }
        return ContainsToken(candidate);
    }

    private bool ContainsImperativePhrase(string candidate)
    {
        if (candidate.Contains(' '))
        {
            if (normalized.Contains(candidate, StringComparison.Ordinal)) return true;
            var candidateTokens = TokenRegex.Matches(candidate).Select(x => x.Value).ToArray();
            for (var start = 0; start <= tokens.Length - candidateTokens.Length; start++)
            {
                if (candidateTokens.Select((token, offset) => tokens[start + offset].Equals(token, StringComparison.Ordinal)).All(x => x))
                    return true;
            }
            return false;
        }
        return tokens.Any(token => token.Equals(candidate, StringComparison.Ordinal));
    }

    private bool ContainsToken(string candidate) => tokens.Any(token => TokensMatch(token, candidate));

    private static bool TokensMatch(string token, string candidate)
    {
        if (token.Equals(candidate, StringComparison.Ordinal)) return true;
        if (candidate.Length >= 3 && token.StartsWith(candidate, StringComparison.Ordinal) && IsKnownSuffix(token[candidate.Length..]))
            return true;

        var tokenStem = StripSuffix(token);
        if (tokenStem.Equals(candidate, StringComparison.Ordinal)) return true;
        if (candidate.Length < 4 || token.Length < 3) return false;

        var maximumDistance = candidate.Length >= 8 ? 2 : 1;
        return BoundedLevenshtein(tokenStem, candidate, maximumDistance) <= maximumDistance;
    }

    private static bool IsKnownSuffix(string value) => value.Length > 0 && OrderedSuffixes.Contains(value, StringComparer.Ordinal);

    private static string StripSuffix(string value)
    {
        foreach (var suffix in OrderedSuffixes)
        {
            if (value.Length - suffix.Length >= 3 && value.EndsWith(suffix, StringComparison.Ordinal))
                return value[..^suffix.Length];
        }
        return value;
    }

    private static int BoundedLevenshtein(string left, string right, int maximum)
    {
        if (Math.Abs(left.Length - right.Length) > maximum) return maximum + 1;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }
            if (rowMinimum > maximum) return maximum + 1;
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
