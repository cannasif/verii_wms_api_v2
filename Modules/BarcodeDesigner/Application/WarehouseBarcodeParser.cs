using System.Globalization;
using System.Text.RegularExpressions;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Application;

public sealed record ParsedWarehouseBarcode(
    string? ProductCode,
    string? LotNo,
    string? SerialNo,
    decimal? Quantity,
    DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate,
    IReadOnlyDictionary<string, string> Segments);

public static partial class WarehouseBarcodeParser
{
    private const char GroupSeparator = (char)29;
    private static readonly IReadOnlyDictionary<string, int> FixedLengths =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["01"] = 14,
            ["11"] = 6,
            ["13"] = 6,
            ["15"] = 6,
            ["17"] = 6
        };
    private static readonly HashSet<string> VariableAis =
        ["10", "21", "22", "30", "37", "240", "241"];

    public static ParsedWarehouseBarcode? TryParse(string raw)
    {
        var original = (raw ?? string.Empty).Trim();
        var value = original;
        if (value.StartsWith("]C1", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("]d2", StringComparison.OrdinalIgnoreCase))
            value = value[3..];
        if (value.Length < 4) return null;

        Dictionary<string, string>? segments = value.Contains('(')
            ? ParseHumanReadable(value)
            : ParseScannerData(value);
        if (segments is null || segments.Count == 0) return null;
        if (!IsStructuredGs1(original, segments)) return null;

        var productCode = First(segments, "240", "241", "01");
        var quantity = ParseQuantity(First(segments, "30", "37"));
        return new ParsedWarehouseBarcode(
            productCode,
            First(segments, "10"),
            First(segments, "21"),
            quantity,
            ParseGs1Date(First(segments, "11", "13")),
            ParseGs1Date(First(segments, "17", "15")),
            segments);
    }

    /// <summary>
    /// Plain stock codes such as 100134-1 start with GS1 AI 10 and would otherwise be
    /// read as a lot. Require a structured GS1 symbol (parentheses, FNC1, symbology
    /// prefix, GTIN/item AI, or more than one AI) before treating the value as GS1.
    /// </summary>
    internal static bool IsStructuredGs1(string raw, IReadOnlyDictionary<string, string> segments)
    {
        if (raw.Contains('(', StringComparison.Ordinal)) return true;
        if (raw.Contains(GroupSeparator)) return true;
        if (raw.StartsWith("]C1", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("]d2", StringComparison.OrdinalIgnoreCase))
            return true;
        if (segments.ContainsKey("01") || segments.ContainsKey("240") || segments.ContainsKey("241"))
            return true;
        return segments.Count > 1;
    }

    private static Dictionary<string, string>? ParseHumanReadable(string value)
    {
        var matches = HumanReadableAi().Matches(value);
        if (matches.Count == 0) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var start = match.Index + match.Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : value.Length;
            var segment = value[start..end].Trim().Trim(GroupSeparator);
            if (segment.Length > 0) result[match.Groups[1].Value] = segment;
        }
        return result;
    }

    private static Dictionary<string, string>? ParseScannerData(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var cursor = 0;
        while (cursor < value.Length)
        {
            if (value[cursor] == GroupSeparator) { cursor++; continue; }
            var ai = DetectAi(value, cursor);
            if (ai is null) return null;
            cursor += ai.Length;

            if (FixedLengths.TryGetValue(ai, out var length))
            {
                if (cursor + length > value.Length) return null;
                result[ai] = value.Substring(cursor, length);
                cursor += length;
                continue;
            }

            var separator = value.IndexOf(GroupSeparator, cursor);
            var end = separator >= 0 ? separator : value.Length;
            if (end == cursor) return null;
            result[ai] = value[cursor..end];
            cursor = separator >= 0 ? separator + 1 : end;
        }
        return result;
    }

    private static string? DetectAi(string value, int cursor)
    {
        if (cursor + 3 <= value.Length)
        {
            var three = value.Substring(cursor, 3);
            if (VariableAis.Contains(three)) return three;
        }
        if (cursor + 2 <= value.Length)
        {
            var two = value.Substring(cursor, 2);
            if (FixedLengths.ContainsKey(two) || VariableAis.Contains(two)) return two;
        }
        return null;
    }

    private static string? First(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return null;
    }

    private static decimal? ParseQuantity(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static DateOnly? ParseGs1Date(string? value)
    {
        if (value is null || value.Length != 6 || !int.TryParse(value, out _)) return null;
        var year = 2000 + int.Parse(value[..2], CultureInfo.InvariantCulture);
        var month = int.Parse(value.Substring(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(value.Substring(4, 2), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return null;
        if (day == 0) day = DateTime.DaysInMonth(year, month);
        if (day > DateTime.DaysInMonth(year, month)) return null;
        return new DateOnly(year, month, day);
    }

    [GeneratedRegex(@"\((\d{2,4})\)", RegexOptions.CultureInvariant)]
    private static partial Regex HumanReadableAi();
}
