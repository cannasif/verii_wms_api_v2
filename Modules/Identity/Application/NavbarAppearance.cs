using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Identity.Application;

public static class NavbarAppearance
{
    public const string ModeSearch = "search";
    public const string ModeKpi = "kpi";
    public const string DefaultKpiKeys = "myTasks,qualityQueue,pendingApproval,erpIssues";
    public const int MaxKpiCount = 4;

    public static readonly HashSet<string> AllowedModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ModeSearch,
        ModeKpi,
    };

    public static readonly HashSet<string> AllowedKpiKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "myTasks",
        "qualityQueue",
        "pendingApproval",
        "erpIssues",
        "openOperations",
        "goodsReceiptToday",
        "shipmentToday",
        "transferToday",
    };

    public static string CoerceMode(string? mode, string fallback = ModeSearch)
    {
        var resolved = string.IsNullOrWhiteSpace(mode) ? fallback : mode.Trim().ToLowerInvariant();
        return AllowedModes.TryGetValue(resolved, out var canonical)
            ? canonical
            : AllowedModes.TryGetValue(fallback, out var fallbackCanonical)
                ? fallbackCanonical
                : ModeSearch;
    }

    public static string NormalizeMode(string? mode, string fallback)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return CoerceMode(fallback);
        var resolved = mode.Trim();
        if (!AllowedModes.TryGetValue(resolved, out var canonical))
            throw AppException.BadRequest("Navbar merkezi geçersiz.");
        return canonical;
    }

    public static string NormalizeKeys(IReadOnlyList<string>? keys, string fallback)
    {
        if (keys is null)
            return string.IsNullOrWhiteSpace(fallback) ? DefaultKpiKeys : fallback;

        var normalized = new List<string>(MaxKpiCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in keys)
        {
            var key = raw?.Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!AllowedKpiKeys.TryGetValue(key, out var canonical))
                throw AppException.BadRequest("Seçilen navbar KPI desteklenmiyor.");
            if (!seen.Add(canonical)) continue;
            normalized.Add(canonical);
            if (normalized.Count > MaxKpiCount)
                throw AppException.BadRequest("Navbar KPI sayısı en fazla 4 olabilir.");
        }

        if (normalized.Count == 0)
            throw AppException.BadRequest("En az bir navbar KPI seçilmelidir.");

        return string.Join(',', normalized);
    }

    public static string[] SplitKeys(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return DefaultKpiKeys.Split(',');

        var keys = stored
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(key => AllowedKpiKeys.TryGetValue(key, out var canonical) ? canonical : null)
            .Where(key => key is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxKpiCount)
            .ToArray();

        return keys.Length == 0 ? DefaultKpiKeys.Split(',') : keys;
    }
}
