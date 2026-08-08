using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed class WarehouseAssistantIntentResolver : IWarehouseAssistantIntentResolver
{
    private static readonly string[] SerialWords = ["seri", "serial", "barkod", "etiket"];
    private static readonly string[] StockWords = ["stok", "urun", "malzeme", "mamul", "parca"];
    private static readonly string[] BalanceWords = ["bakiye", "miktar", "kac", "nerede", "hangi depo", "hangi raf", "lokasyon", "konum"];
    private static readonly string[] ReceiptWords = ["mal kabul", "irsaliye", "iceri", "giris", "alindi", "kabul edildi", "ne zaman", "kim tarafindan", "kim aldi"];
    private static readonly string[] ActivityWords = ["islem", "hareket", "yaptigim", "yapmis", "yapti", "aktivit", "kayit"];

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
        var requestsAll = ContainsAny(normalized, ["herkes", "tum kullanici", "butun kullanici", "tum personel", "ekipteki herkes"]);

        WarehouseAssistantIntent intent;
        decimal confidence;
        if (ContainsAny(normalized, ["ne sorabilirim", "yardim", "ornek soru", "neler yapabilirsin"]))
        {
            intent = WarehouseAssistantIntent.Help;
            confidence = 1m;
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
        if (normalized.Contains("dun", StringComparison.Ordinal)) return WarehouseAssistantDatePreset.Yesterday;
        if (normalized.Contains("bu hafta", StringComparison.Ordinal)) return WarehouseAssistantDatePreset.ThisWeek;
        if (ContainsAny(normalized, ["son 30 gun", "son otuz gun", "bu ay"])) return WarehouseAssistantDatePreset.LastThirtyDays;
        if (ContainsAny(normalized, ["son 7 gun", "son yedi gun"])) return WarehouseAssistantDatePreset.LastSevenDays;
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

    private static bool IsSerialStopWord(string value) =>
        new[] { "bu", "bakiye", "nerede", "miktar", "ne", "kim", "hangi", "kac" }
            .Any(value.StartsWith);

    private static bool ContainsAny(string value, IEnumerable<string> candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
}
